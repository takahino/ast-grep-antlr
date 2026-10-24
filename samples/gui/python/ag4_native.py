"""ctypes binding for ag4_native shared library."""

from __future__ import annotations

import ctypes
import json
import os
from pathlib import Path


def resolve_lib_dir() -> Path:
    env = os.environ.get("AG4_NATIVE_DIR")
    if env and Path(env).is_dir():
        return Path(env).resolve()

    here = Path.cwd()
    for candidate in (
        here / "lib",
        here / "../lib",
        here / "../../lib",
        here / "samples/gui/lib",
        Path(__file__).resolve().parent.parent / "lib",
    ):
        dll = candidate / "ag4_native.dll"
        so = candidate / "ag4_native.so"
        if dll.is_file() or so.is_file():
            return candidate.resolve()
    return (Path(__file__).resolve().parent.parent / "lib").resolve()


def default_fixtures_dir() -> Path:
    fixtures = resolve_lib_dir().parent / "fixtures"
    return fixtures if fixtures.is_dir() else resolve_lib_dir()


class Ag4Native:
    def __init__(self, lib_dir: Path | None = None) -> None:
        lib_dir = lib_dir or resolve_lib_dir()
        name = "ag4_native.dll" if os.name == "nt" else "ag4_native.so"
        path = lib_dir / name
        if not path.is_file():
            raise FileNotFoundError(
                f"Native library not found at {path}. Publish with:\n"
                "  dotnet publish src/AstGrepAntlr.Native -c Release -r win-x64 -o samples/gui/lib\n"
                "Or set AG4_NATIVE_DIR."
            )
        self._lib = ctypes.CDLL(str(path))
        self._lib.ag4_version.restype = ctypes.c_void_p
        self._lib.ag4_search.argtypes = [ctypes.c_char_p, ctypes.POINTER(ctypes.c_void_p)]
        self._lib.ag4_search.restype = ctypes.c_int
        self._lib.ag4_free.argtypes = [ctypes.c_void_p]

    def version(self) -> str:
        ptr = self._lib.ag4_version()
        return ctypes.string_at(ptr).decode("utf-8") if ptr else ""

    def search(self, request: dict) -> tuple[int, str]:
        payload = json.dumps(request).encode("utf-8")
        out = ctypes.c_void_p()
        rc = self._lib.ag4_search(payload, ctypes.byref(out))
        try:
            text = ctypes.string_at(out).decode("utf-8") if out else ""
            return rc, text
        finally:
            if out:
                self._lib.ag4_free(out)
