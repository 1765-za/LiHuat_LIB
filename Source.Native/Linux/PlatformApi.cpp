#include "PlatformApi.h"
#include "LinuxPlatform.h"
namespace p = lihuat::linux_platform;
uint32_t LihuatPlatform_ABI() { return 1; }
size_t LihuatPlatform_PageSize() { return p::PageSize(); }
int LihuatPlatform_Query(const void* address, LihuatPlatformRegion* result) {
    if (!result) return 0;
    p::Region region;
    if (!p::Query(address, region)) return 0;
    *result = {region.begin, region.end, static_cast<uint32_t>(region.protection), region.privateMapping ? 1u : 0u};
    return 1;
}
int LihuatPlatform_Read(const void* address, void* result, size_t bytes) { return p::TryCopy(address, result, bytes) ? 1 : 0; }
int LihuatPlatform_ImageContains(const void* anchor, const void* address, size_t bytes) { return p::ImageContains(anchor, address, bytes) ? 1 : 0; }
