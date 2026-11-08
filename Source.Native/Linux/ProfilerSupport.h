#pragma once
#include <stdint.h>
#define LIHUAT_PROFILER_EXPORT __attribute__((visibility("default")))
#ifdef __cplusplus
extern "C" {
#endif
/* Separate support ABI, NOT LihuatNative ABI 11. HRESULT values are signed 32-bit. */
LIHUAT_PROFILER_EXPORT uint32_t LihuatProfiler_GetSupportAbi(void);
LIHUAT_PROFILER_EXPORT int32_t LihuatProfiler_IsReady(void);
/* Only call on a profiler-owned, unmanaged OS thread, never a managed/PInvoke thread.
   Nested suspension is supported only on the owning thread. Resume on that thread.
   The thread must not run managed code, block on managed work, or unload this DSO. */
LIHUAT_PROFILER_EXPORT int32_t LihuatProfiler_SuspendRuntime(void);
LIHUAT_PROFILER_EXPORT int32_t LihuatProfiler_ResumeRuntime(void);
#ifdef __cplusplus
}
#endif
