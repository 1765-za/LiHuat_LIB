#!/usr/bin/env python3
"""Verify the actual ELF artifact and its limited public support ABI."""
import ctypes
import pathlib
import errno
import os
import subprocess
import sys

library = pathlib.Path(sys.argv[1]).resolve()
header = subprocess.check_output(["readelf", "-h", str(library)], text=True)
assert "ELF64" in header and "Advanced Micro Devices X86-64" in header
assert "DYN (Shared object file)" in header
symbols = subprocess.check_output(["nm", "-D", "--defined-only", str(library)], text=True)
assert "LihuatNative_" not in symbols, "Support library must never impersonate ABI 11"
assert "LihuatPlatform_ABI" in symbols
imports = subprocess.check_output(["readelf", "-d", str(library)], text=True)
assert ".dll" not in imports.lower()
lib = ctypes.CDLL(str(library), use_errno=True)
lib.LihuatPlatform_ABI.restype = ctypes.c_uint32
assert lib.LihuatPlatform_ABI() == 1
lib.LihuatPlatform_Read.argtypes = (ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t)
lib.LihuatPlatform_Read.restype = ctypes.c_int
source = ctypes.c_uint64(0x123456789abcdef0)
result = ctypes.c_uint64()
read = lib.LihuatPlatform_Read(ctypes.byref(source), ctypes.byref(result), 8)
if read == 0 and ctypes.get_errno() in (errno.EPERM, errno.ENOSYS) and os.environ.get("LIHUAT_ALLOW_RESTRICTED_TESTS") == "1":
    print("SKIP positive ctypes read: process_vm_readv unavailable in host, errno=" + str(ctypes.get_errno()))
else:
    assert read == 1 and source.value == result.value
assert lib.LihuatPlatform_Read(ctypes.c_void_p(1), ctypes.byref(result), 8) == 0
try:
    lib.LihuatNative_Initialize
except AttributeError:
    pass
else:
    raise AssertionError("Foundation must not expose runtime initialization")
print("PASS ELF64 x86-64 shared object, Linux dependencies, ctypes load/read rejection, intentionally distinct support ABI")
print("Dependencies:\n" + "\n".join(line.strip() for line in imports.splitlines() if "NEEDED" in line))
