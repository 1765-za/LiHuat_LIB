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

# Optional real profiler-support tests. They still do not build the full ABI-11 engine.
CLR_ROOT ?= build/deps/dotnet-runtime
CLR_CPPFLAGS := -DPAL_STDCPP_COMPAT -DPLATFORM_UNIX -DHOST_64BIT -DHOST_AMD64 \
 -isystem "$(CLR_ROOT)/src/native" -isystem "$(CLR_ROOT)/src/coreclr/pal/inc" \
 -isystem "$(CLR_ROOT)/src/coreclr/pal/inc/rt" -isystem "$(CLR_ROOT)/src/coreclr/pal/prebuilt/inc" \
 -isystem "$(CLR_ROOT)/src/coreclr/pal/prebuilt/idl" -isystem "$(CLR_ROOT)/src/coreclr/inc"
.PHONY: check-clr-inputs profiler profiler-contracts profiler-integration jit-contracts
check-clr-inputs:
	@test -f "$(CLR_ROOT)/src/coreclr/pal/prebuilt/inc/corprof.h" || { echo 'Fetch official headers: bash tools/fetch_clr_headers.sh; or set CLR_ROOT.' >&2; exit 1; }
	@test "$$(git -C "$(CLR_ROOT)" rev-parse HEAD)" = 60629d14374c56f1cb51819049ad1fa529307f8d || { echo 'Unexpected CLR header revision' >&2; exit 1; }
	@git -C "$(CLR_ROOT)" diff --quiet HEAD -- src/coreclr src/native/minipal || { echo 'CLR headers differ from pinned source' >&2; exit 1; }
	python3 tools/generate_clr_callback_base.py "$(CLR_ROOT)/src/coreclr/pal/prebuilt/inc/corprof.h" --check
profiler: $(BUILD)/libLihuatProfilerSupport.so
$(BUILD)/libLihuatProfilerSupport.so: Source.Native/Linux/ProfilerSupport.cpp Source.Native/Linux/ProfilerSupport.h Source.Native/Waylith/ClrCallbackBase.h Source.Native/Linux/profiler-exports.map | $(BUILD) check-clr-inputs
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $(CLR_CPPFLAGS) -shared $< $(LDFLAGS) -Wl,-z,defs,-z,relro,-z,now -Wl,--version-script=Source.Native/Linux/profiler-exports.map -o $@
$(BUILD)/libProfilerHarness.so: tests/profiler/harness.cpp $(BUILD)/libLihuatProfilerSupport.so
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) -shared $< -L$(BUILD) -lLihuatProfilerSupport $(LDFLAGS) -Wl,-z,defs -o $@
$(BUILD)/profiler_contracts: tests/profiler/contracts.cpp $(BUILD)/libLihuatProfilerSupport.so | check-clr-inputs
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $(CLR_CPPFLAGS) $< -L$(BUILD) -lLihuatProfilerSupport $(LDFLAGS) -o $@
profiler-contracts: $(BUILD)/profiler_contracts
	LD_LIBRARY_PATH="$(abspath $(BUILD))$${LD_LIBRARY_PATH:+:$$LD_LIBRARY_PATH}" $(BUILD)/profiler_contracts
profiler-integration: profiler-contracts $(BUILD)/libProfilerHarness.so
	bash tests/profiler/run.sh "$(BUILD)"
$(BUILD)/SourceContract.h: tools/extract_jit_contract.py Source.Native/Waylith/ClrInlining.cpp Source.Native/Waylith/ClrCompilation.cpp | $(BUILD)
	python3 tools/extract_jit_contract.py $@
$(BUILD)/jit_contracts: tests/native/jit_contract_tests.cpp $(BUILD)/SourceContract.h Source.Native/Waylith/GenericBodies.h Source.Native/Waylith/ClrInlining.h | check-clr-inputs
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $(CLR_CPPFLAGS) -DTARGET_AMD64 -I$(BUILD) $< -o $@
jit-contracts: $(BUILD)/jit_contracts
	$(BUILD)/jit_contracts
