#include "LinuxPlatform.h"
#include "../../Source.Native/Waylith/EntryWriteIndex.h"
#include <array>
#include <atomic>
#include <cassert>
#include <cstring>
#include <cerrno>
#include <cstdlib>
#include <iostream>
#include <mutex>
#include <set>
#include <thread>
#include <vector>
#include <sys/mman.h>
#include <unistd.h>
namespace p = lihuat::linux_platform;
bool restricted = false;
bool AllowRestricted() { const char* value = std::getenv("LIHUAT_ALLOW_RESTRICTED_TESTS"); return value && std::strcmp(value, "1") == 0; }


struct Mapping {
    uint8_t* data;
    size_t bytes;
    explicit Mapping(size_t length, int protection = PROT_READ | PROT_WRITE) : bytes(length) {
        void* result = mmap(nullptr, bytes, protection, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
        assert(result != MAP_FAILED); data = static_cast<uint8_t*>(result);
    }
    ~Mapping() { if (data) assert(munmap(data, bytes) == 0); }
};
struct Receiver { void** table; int64_t base; };
struct Proxy { void** table; Receiver* original; };
int64_t IntegerTarget(Receiver* r, int64_t a, int64_t b, int64_t c, int64_t d, int64_t e, int64_t f, int64_t g) {
    return r->base + a + 2*b + 3*c + 4*d + 5*e + 6*f + 7*g;
}
double FloatingTarget(Receiver* r, double a, double b, double c, double d, double e, double f, double g, double h, double i) {
    return static_cast<double>(r->base) + a + 2*b + 3*c + 4*d + 5*e + 6*f + 7*g + 8*h + 9*i;
}
int Destination() { return 42; }

void TestMemory() {
    size_t page = p::PageSize();
    assert(page > 0 && page % 8 == 0);
    Mapping memory(page * 2);
    std::memcpy(memory.data, "lihuat", 7);
    char output[7]{};
    errno = 0;
    bool read = p::TryCopy(memory.data, output, sizeof(output));
    if (!read && (errno == EPERM || errno == ENOSYS) && AllowRestricted()) {
        restricted = true;
        std::cout << "SKIP positive process_vm_readv: host denied syscall, errno=" << errno << "\n";
    } else assert(read && std::strcmp(output, "lihuat") == 0);
    assert(!p::TryCopy(nullptr, output, sizeof(output)));
    assert(!p::TryCopy(reinterpret_cast<void*>(1), output, sizeof(output)));
    assert(!p::TryCopy(memory.data, reinterpret_cast<void*>(1), sizeof(output)));
    assert(!p::TryCopy(memory.data, output, SIZE_MAX));
    p::Region region;
    assert(p::Query(memory.data, region) && (region.protection & PROT_WRITE));
    assert(p::Readable(memory.data, page * 2) && !p::Readable(memory.data, page, true));
    assert(!p::Readable(memory.data, SIZE_MAX));
    assert(mprotect(memory.data + page, page, PROT_NONE) == 0);
    assert(!p::Readable(memory.data + page, 1));
    assert(!p::TryCopy(memory.data + page, output, 1));
    assert(!p::TryCopy(memory.data + page - 3, output, 7));
    assert(!p::Readable(memory.data + page - 3, 7));
    assert(mprotect(memory.data + page, page, PROT_READ | PROT_WRITE) == 0);
    std::array<uint8_t, 1024> random{};
    assert(p::RandomBytes(random.data(), random.size()));
    bool nonzero = false; for (auto value : random) nonzero |= value != 0;
    assert(nonzero && !p::RandomBytes(nullptr, 10));
    assert(!p::Query(nullptr, region));
    std::cout << "PASS memory query, read rejection, overflow, inaccessible page, random\n";
}
void TestRelaysAndExchange() {
    Mapping code(p::PageSize());
    // mov eax,1; ret; nop; nop
    uint8_t original[8]{0xb8, 1, 0, 0, 0, 0xc3, 0x90, 0x90};
    std::memcpy(code.data, original, 8);
    int64_t expected; std::memcpy(&expected, original, 8);
    assert(mprotect(code.data, code.bytes, PROT_READ | PROT_EXEC) == 0);
    auto call = reinterpret_cast<int(*)()>(code.data);
    assert(call() == 1);
    void** relay = p::AllocateRelay(reinterpret_cast<void*>(&Destination));
    assert(relay && reinterpret_cast<uintptr_t>(relay) < 0x80000000u);
    int64_t branch = 0;
    assert(p::Branch(relay, branch));
    assert(!p::Branch(nullptr, branch));
    assert(!p::Branch(reinterpret_cast<void**>(0x80000000u), branch));
    assert(!p::ExchangeUnpublished(code.data + 1, code.data + 1, expected, branch));
    assert(!p::ExchangeUnpublished(code.data, code.data, expected ^ 1, branch));
    assert(call() == 1);
    p::Region region;
    assert(p::Query(code.data, region) && !(region.protection & PROT_WRITE));
    assert(p::ExchangeUnpublished(code.data, code.data, expected, branch));
    assert(call() == 42);
    assert(p::Query(code.data, region) && region.protection == (PROT_READ | PROT_EXEC));
    assert(p::ExchangeUnpublished(code.data, code.data, branch, expected));
    assert(call() == 1);
    Mapping other(p::PageSize());
    assert(!p::ExchangeUnpublished(code.data, other.data, expected, branch));
    std::mutex lock;
    std::set<void**> slots;
    std::vector<std::thread> threads;
    for (int i=0; i<4; ++i) threads.emplace_back([&] {
        for (int j=0; j<64; ++j) {
            void** slot = p::AllocateRelay(reinterpret_cast<void*>(&Destination));
            assert(slot && *slot == reinterpret_cast<void*>(&Destination));
            std::lock_guard<std::mutex> guard(lock); assert(slots.insert(slot).second);
        }
    });
    for (auto& thread : threads) thread.join();
    assert(slots.size() == 256);
    std::cout << "PASS low-address relays, actual branch execution, CAS failure/restore, multithread allocation\n";
}
void TestForwarders() {
    p::Forwarders thunks;
    assert(!p::CreateRdiReceiverForwarders(0, thunks));
    assert(!p::CreateRdiReceiverForwarders(4097, thunks));
    assert(p::CreateRdiReceiverForwarders(2, thunks));
    assert(!p::CreateRdiReceiverForwarders(2, thunks));
    void* methods[] = {reinterpret_cast<void*>(&IntegerTarget), reinterpret_cast<void*>(&FloatingTarget)};
    Receiver receiver{methods, 100};
    Proxy proxy{nullptr, &receiver};
    auto integers = reinterpret_cast<int64_t(*)(Proxy*,int64_t,int64_t,int64_t,int64_t,int64_t,int64_t,int64_t)>(thunks.code);
    auto floating = reinterpret_cast<double(*)(Proxy*,double,double,double,double,double,double,double,double,double)>(thunks.code + 16);
    assert(integers(&proxy,1,2,3,4,5,6,7) == IntegerTarget(&receiver,1,2,3,4,5,6,7));
    assert(floating(&proxy,1,2,3,4,5,6,7,8,9) == FloatingTarget(&receiver,1,2,3,4,5,6,7,8,9));
    p::Region region;
    assert(p::Query(thunks.code, region) && region.protection == (PROT_READ | PROT_EXEC));
    assert(p::ReleaseUnpublishedForwarders(thunks));
    assert(!thunks.code && !p::ReleaseUnpublishedForwarders(thunks));
    std::cout << "PASS SysV receiver, register/stack integer and FP arguments, RX permissions\n";
}
void TestImagesAndIndex() {
    auto anchor = reinterpret_cast<void*>(&Destination);
    assert(p::ImageContains(anchor, anchor, 1));
    int stack = 0;
    assert(!p::ImageContains(anchor, &stack, sizeof(stack)));
    assert(!p::ImageContains(anchor, anchor, SIZE_MAX));
    assert(!p::FindLoadedSymbol("liblihuat-definitely-not-loaded.so", "getJit"));
    assert(p::FindLoadedSymbol("libc.so.6", "getpid"));
    lihuat::EntryWriteIndex index;
    index.Add(reinterpret_cast<void*>(0x1000), 0x20);
    index.Add(reinterpret_cast<void*>(0x1020), 0x20);
    index.Add(reinterpret_cast<void*>(0x1100), 0x10);
    index.Seal();
    assert(index.Overlaps(reinterpret_cast<void*>(0x1030), 1));
    assert(!index.Overlaps(reinterpret_cast<void*>(0x1040), 0xc0));
    assert(!index.Overlaps(reinterpret_cast<void*>(UINTPTR_MAX - 1), 4));
    std::cout << "PASS ELF image ranges, loaded-module symbols, existing EntryWriteIndex\n";
}
int main() {
    TestMemory(); TestRelaysAndExchange(); TestForwarders(); TestImagesAndIndex();
    std::cout << (restricted ? "Linux platform tests passed with explicitly reported syscall SKIP. " : "All Linux platform tests passed. ")
              << "Full ABI-11 runtime is NOT under test.\n";
}
