#include "ProfilerSupport.h"
#include <atomic>
#include <chrono>
#include <thread>
#include <cstdio>
namespace { std::atomic<unsigned long> ticks{0}; }
extern "C" LIHUAT_PROFILER_EXPORT void Tick() { ++ticks; }
extern "C" LIHUAT_PROFILER_EXPORT int RunSuspensionTest() {
    int result = 0;
    std::thread worker([&] {
        auto pause = [] { std::this_thread::sleep_for(std::chrono::milliseconds(40)); };
        int32_t hr = LihuatProfiler_SuspendRuntime();
        if (hr < 0) { std::fprintf(stderr,"SuspendRuntime failed: 0x%08x\n",static_cast<unsigned>(hr)); result = 1; return; }
        if (LihuatProfiler_SuspendRuntime() < 0) result = 2;
        // A second unmanaged thread cannot steal or resume this scope.
        std::thread other([&] {
            if (LihuatProfiler_SuspendRuntime() >= 0 || LihuatProfiler_ResumeRuntime() >= 0) result = 3;
        });
        other.join();
        pause(); // Any PInvoke already in flight can finish.
        auto before = ticks.load();
        pause();
        if (ticks.load() != before) result = 4;
        if (LihuatProfiler_ResumeRuntime() < 0) result = 5;
        pause();
        if (ticks.load() != before) result = 6; // Nested scope still holds.
        if (LihuatProfiler_ResumeRuntime() < 0) result = 7;
        if (LihuatProfiler_ResumeRuntime() >= 0) result = 8;
    });
    worker.join();
    if (result) return result;
    // Exercise the TLS destructor: an abandoned scope is resumed on thread exit.
    std::thread abandon([&] {
        if (LihuatProfiler_SuspendRuntime() < 0) result = 9;
    });
    abandon.join();
    return result;
}
