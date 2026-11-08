#pragma once
#include <stddef.h>
#include <stdint.h>
#if defined(__GNUC__)
#define LIHUAT_PLATFORM_API __attribute__((visibility("default")))
#else
#define LIHUAT_PLATFORM_API
#endif
#ifdef __cplusplus
extern "C" {
#endif
// This is a development platform-support ABI, NOT the LihuatNative ABI 11.
// It deliberately exports no LihuatNative_* entry points.
typedef struct LihuatPlatformRegion {
    uint64_t begin, end;
    uint32_t protection, private_mapping;
} LihuatPlatformRegion;
LIHUAT_PLATFORM_API uint32_t LihuatPlatform_ABI(void);
LIHUAT_PLATFORM_API size_t LihuatPlatform_PageSize(void);
LIHUAT_PLATFORM_API int LihuatPlatform_Query(const void* address, LihuatPlatformRegion* result);
LIHUAT_PLATFORM_API int LihuatPlatform_Read(const void* address, void* result, size_t bytes);
LIHUAT_PLATFORM_API int LihuatPlatform_ImageContains(const void* anchor, const void* address, size_t bytes);
#ifdef __cplusplus
}
#endif
