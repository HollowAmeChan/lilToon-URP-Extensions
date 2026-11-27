import argparse
import ctypes
import json
import re
from pathlib import Path


def disassemble(data):
    compiler = ctypes.WinDLL("d3dcompiler_47.dll")
    disassemble_shader = compiler.D3DDisassemble
    disassemble_shader.argtypes = (
        ctypes.c_void_p, ctypes.c_size_t, ctypes.c_uint, ctypes.c_char_p,
        ctypes.POINTER(ctypes.c_void_p),
    )
    disassemble_shader.restype = ctypes.c_long
    blob = ctypes.c_void_p()
    result = disassemble_shader(data, len(data), 0, None, ctypes.byref(blob))
    if result or not blob:
        raise RuntimeError(f"D3DDisassemble failed ({result:#x})")

    vtable = ctypes.cast(blob, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
    try:
        pointer = ctypes.WINFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p)(vtable[3])(blob)
        size = ctypes.WINFUNCTYPE(ctypes.c_size_t, ctypes.c_void_p)(vtable[4])(blob)
        return ctypes.string_at(pointer, size).decode("utf-8")
    finally:
        ctypes.WINFUNCTYPE(ctypes.c_uint, ctypes.c_void_p)(vtable[2])(blob)


def measure(assembly):
    profile = re.search(r"(?m)^\s*(ps_\d+_\d+)\s*$", assembly)
    if not profile:
        raise ValueError("Expected a pixel shader profile")
    opcodes = re.findall(r"(?m)^[ \t]*(?:\d+:[ \t]*)?([a-z][a-z0-9_]*)\b",
                         assembly[profile.end():])
    samplers = set(re.findall(r"dcl_sampler\s+s(\d+)\b", assembly))
    return {
        "profile": profile.group(1),
        "samples": sum(op.startswith(("sample", "gather")) for op in opcodes),
        "loops": opcodes.count("loop"),
        "samplers": len(samplers),
    }


def main():
    parser = argparse.ArgumentParser(description="Verify fixed PCF fetch budgets in the Unity probe's DXBC.")
    parser.add_argument("report_directory", type=Path)
    directory = parser.parse_args().report_directory
    expected = {"hard": 1, "soft": 9, "runtime-toggle": 9}
    metrics = []
    rows = json.loads((directory / "fragment-compile.json").read_text(encoding="utf-8-sig"))["rows"]
    if len(rows) != len(expected) or {row["sample"] for row in rows} != expected.keys():
        raise ValueError("Expected one hard, one soft, and one runtime-toggle compile")
    for index, row in enumerate(rows):
        if not row["success"] or row["messages"]:
            raise ValueError(f"Compile diagnostics for {row['sample']}: {row['messages']}")
        measured = measure(disassemble((directory / f"{index:02}.dxbc").read_bytes()))
        if measured["samples"] != expected[row["sample"]] or measured["loops"] or measured["samplers"] != 1:
            raise ValueError(f"Unexpected DXBC budget for {row['sample']}: {measured}")
        metrics.append({"variant": row["sample"], **measured})
    print(json.dumps(metrics, indent=2))


if __name__ == "__main__":
    main()
