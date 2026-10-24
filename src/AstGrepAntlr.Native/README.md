# AstGrepAntlr.Native (`ag4_native`)

NativeAOT shared library exposing a small C ABI for embedding pattern search.

## Build (Windows x64)

```bash
dotnet publish src/AstGrepAntlr.Native/AstGrepAntlr.Native.csproj \
  -c Release -r win-x64 -o samples/gui/lib
```

Produces `ag4_native.dll` (plus companion files) under the output directory.

## C ABI

See [samples/gui/include/ag4_native.h](../../samples/gui/include/ag4_native.h).

| Symbol        | Description                                                                       |
| ------------- | --------------------------------------------------------------------------------- |
| `ag4_version` | UTF-8 version string (`ag4_free` required for the returned pointer)               |
| `ag4_search`  | JSON request (`paths` or in-memory `sources`) → match array; free with `ag4_free` |
| `ag4_free`    | Frees buffers returned by `ag4_search` / `ag4_version`                            |

Sample GUIs that load this DLL: [samples/gui/README.md](../../samples/gui/README.md).
