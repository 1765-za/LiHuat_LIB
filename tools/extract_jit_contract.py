#!/usr/bin/env python3
"""Extract the current source's private JIT layout assumptions for compiler tests.

This is a source-contract check, not validation of an installed game runtime.
The generated header is a build-only artifact; it never alters production code.
"""
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parents[1]
inline = (root / "Source.Native/Waylith/ClrInlining.cpp").read_text()
compile_source = (root / "Source.Native/Waylith/ClrCompilation.cpp").read_text()

def exactly(pattern, source, label):
    found = re.findall(pattern, source, re.DOTALL)
    if len(found) != 1:
        raise SystemExit("Cannot uniquely extract " + label + "; review the JIT contract test")
    return found[0]

slots = exactly(r"(constexpr size_t InfoSlots\s*=\s*\d+[^;]*;)", inline, "vtable slots")
allocation = exactly(r"(struct Allocation\s*\{[^}]+\};)", inline, "allocation layout")
guid = exactly(r"const GUID expected\s*=\s*(\{[^;]+\});", compile_source, "JIT GUID")
out = pathlib.Path(sys.argv[1])
out.parent.mkdir(parents=True, exist_ok=True)
out.write_text("// Generated from this checkout by tools/extract_jit_contract.py.\n#pragma once\n"
               "namespace source_contract {\n" + slots + "\n" + allocation
               + "\nconstexpr GUID expected = " + guid + ";\n}\n")
