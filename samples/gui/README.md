# Sample GUIs (DLL load)

These samples load the NativeAOT shared library `ag4_native` via each language’s FFI
(P/Invoke, JNA, ctypes) — not the `ag4` CLI.

## 1. Publish the native library (Windows x64)

From the repository root:

```bash
dotnet publish src/AstGrepAntlr.Native/AstGrepAntlr.Native.csproj \
  -c Release -r win-x64 -o samples/gui/lib
```

You should see `samples/gui/lib/ag4_native.dll`.

Also ensure tree-sitter natives (`tree-sitter*.dll` / `.so` / `.dylib` from
TreeSitter.DotNet `runtimes/<rid>/native/`) sit beside `ag4_native` so Java / C++ / Python
use the default tree-sitter backend. If they are missing, ag4 falls back to ANTLR.

Optional: `set AG4_NATIVE_DIR=C:\path\to\samples\gui\lib`

Header: [include/ag4_native.h](include/ag4_native.h)

## 2. Fixtures

[fixtures/](fixtures/) contains tiny `Demo.java` / `Demo.cs` with `foo(1, 2)` for smoke searches.

## 3. C# (WinForms + P/Invoke)

Requires .NET 10 SDK (Windows).

```bash
dotnet run --project samples/gui/csharp
# headless smoke:
dotnet run --project samples/gui/csharp -- --smoke
```

## 4. Java (Swing + JNA)

Requires JDK 17+ (tested on 25). Download JNA once:

```bash
mkdir samples/gui/java/lib
curl -L -o samples/gui/java/lib/jna.jar \
  https://repo1.maven.org/maven2/net/java/dev/jna/jna/5.17.0/jna-5.17.0.jar
```

Build and run (from `samples/gui/java`):

```bash
cd samples/gui/java
javac -cp lib/jna.jar *.java
java --enable-native-access=ALL-UNNAMED -cp ".;lib/jna.jar" -Djna.library.path=../lib MainFrame
# headless smoke:
java --enable-native-access=ALL-UNNAMED -cp ".;lib/jna.jar" -Djna.library.path=../lib MainFrame --smoke
```

On PowerShell use `;` instead of `:` in `-cp` as shown above.

## 5. Python (tkinter + ctypes)

Requires Python 3.10+ with tkinter.

```bash
cd samples/gui/python
python main.py
# headless smoke:
python main.py --smoke
```

## Request JSON shape

Filesystem (walk `paths`):

```json
{
  "pattern": "foo($$$ARGS)",
  "language": "java",
  "paths": ["samples/gui/fixtures"],
  "strictness": "smart",
  "maxResults": 100
}
```

In-memory buffers (frontend-collected strings; when `sources` is non-empty, `paths` is ignored):

```json
{
  "pattern": "foo($$$ARGS)",
  "language": "java",
  "sources": [
    {
      "path": "snippet.java",
      "content": "class Demo { void m() { foo(1, 2); } }\n"
    }
  ],
  "strictness": "smart",
  "maxResults": 100
}
```

`sources[].path` is a label only (shown as match `file`); nothing is read from disk.

Response is a JSON array of matches (same general shape as `ag4 run --json`).

The C# sample GUI can switch **Path** / **Source** mode to try both.
