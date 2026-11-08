# Linux x86_64 platform port. This does NOT build the full ABI-11 runtime yet.
CXX ?= c++
BUILD ?= build/linux-x64
CPPFLAGS += -ISource.Native/Linux
CXXFLAGS ?= -O2 -g
CXXFLAGS += -std=c++17 -Wall -Wextra -Wpedantic -Werror -fPIC -fvisibility=hidden -pthread
LDFLAGS += -pthread -ldl
PLATFORM_SOURCES := Source.Native/Linux/Memory.cpp Source.Native/Linux/EntryRelays.cpp Source.Native/Linux/InliningThunks.cpp Source.Native/Linux/RuntimeModules.cpp
OBJECTS := $(patsubst Source.Native/Linux/%.cpp,$(BUILD)/%.o,$(PLATFORM_SOURCES))

.PHONY: all platform test sanitize verify clean runtime
all: platform
platform: $(BUILD)/libLihuatPlatform.so
$(BUILD):
	mkdir -p $@
$(BUILD)/%.o: Source.Native/Linux/%.cpp Source.Native/Linux/LinuxPlatform.h | $(BUILD)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) -MMD -MP -c $< -o $@
$(BUILD)/libLihuatPlatform.so: $(OBJECTS) $(BUILD)/PlatformApi.o Source.Native/Linux/exports.map
	$(CXX) -shared -Wl,-z,defs,-z,relro,-z,now -Wl,--version-script=Source.Native/Linux/exports.map $(filter %.o,$^) $(LDFLAGS) -o $@
$(BUILD)/platform_tests: tests/native/platform_tests.cpp $(OBJECTS)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@
test: platform $(BUILD)/platform_tests
	$(BUILD)/platform_tests
	python3 tests/native/verify_shared_library.py $(BUILD)/libLihuatPlatform.so
sanitize:
	$(MAKE) BUILD=build/linux-x64-sanitize CXXFLAGS='-std=c++17 -O1 -g -Wall -Wextra -Wpedantic -Werror -fPIC -fvisibility=hidden -pthread -fsanitize=address,undefined -fno-omit-frame-pointer' LDFLAGS='-pthread -ldl -fsanitize=address,undefined' build/linux-x64-sanitize/platform_tests
	ASAN_OPTIONS=$${ASAN_OPTIONS:-detect_leaks=1} build/linux-x64-sanitize/platform_tests
verify: test sanitize
runtime:
	@echo 'Full LihuatNative ABI 11 Linux runtime is not implemented. See docs/linux-port.md.' >&2
	@exit 1
clean:
	rm -rf -- build/linux-x64 build/linux-x64-sanitize
-include $(OBJECTS:.o=.d) $(BUILD)/PlatformApi.d
