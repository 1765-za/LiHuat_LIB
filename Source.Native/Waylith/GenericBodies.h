#pragma once
#include <cstdint>
#include <cstddef>

namespace lihuat {
struct MethodBodyInput { void* method; void* scope; const uint8_t* il; uint32_t length; };
bool HasGenericBodies();
bool GenericInlineBlocked(void* context, void* method);
bool GenericBodyAllowed(void* context, const MethodBodyInput& method);
bool GenericBodyOverlaps(const void* address, size_t length);
bool GenericBodyCompiled(void* context, void* method, void* code, uint32_t size);
bool GenericProfilerMutationAllowed(uint32_t count, const uintptr_t* modules, const uint32_t* methods);
}
