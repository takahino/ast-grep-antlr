# ast-grep-antlr (`ag4`)

**Unofficial** structural search tool inspired by [ast-grep](https://ast-grep.github.io/), powered by [ANTLR4](https://www.antlr.org/) instead of tree-sitter.

This project is **not** affiliated with the official ast-grep project.

## Features

- Metavariable patterns: `$A`, `$_`, `$$$ARGS`, plus ANTLR extension `${A:expression}`
- YAML rules: `pattern`, `kind`, `inside`, `has`, `precedes`, `follows`, `all`/`any`/`not`, `constraints`, `matches`, `fix`, `files`/`ignores`
- `stopBy`: `neighbor` (default, ast-grep-compatible), `end`, or a kind name
- Continues after syntax errors (SLL → LL + error recovery)
- Parallel file walk with literal prefilter, gitignore, and `--globs`
- Encoding preservation (UTF-8/BOM, UTF-16/32, Shift_JIS, EUC-JP) via `--encoding` / `sgconfig.yml`
- Custom DSLs via `.g4` / `.interp` (`sgconfig.yml` → `customLanguages`, lazy-loaded)
- MCP server: `ag4 mcp` (stdio) for AI agents
- Built-in languages: **Java**, **C++** (CPP14), **C#** (Roslyn), **SQLite** (more via custom grammars)

## Install

```bash
# from source
dotnet tool install -g AstGrepAntlr.Cli --add-source ./artifacts/nupkg

# or run
dotnet run --project src/AstGrepAntlr.Cli -- run -p 'System.out.println($A)' -l java src/
```

NativeAOT binaries are published on GitHub Releases (`win-x64` / `linux-x64` / `osx-arm64`).

## Quick start

```bash
ag4 run -p 'System.out.println($A)' -l java path/to/src
ag4 run -p 'System.out.println($A)' -l java --json path/to/src
ag4 run -p 'foo($A)' -l java --strictness smart --max-results 20 --globs '**/*.java' src/
ag4 run -p 'foo($A)' -l java --rewrite 'bar($A)' --apply src/
ag4 scan -r rules/no-println.yml path/to/src
ag4 scan -c sgconfig.yml --error-on warning path/to/src
ag4 debug parse -l java --code 'class A {}'
ag4 debug pattern -l java -p 'System.out.println($A)'
ag4 version
ag4 mcp --root .                 # read-only MCP
ag4 mcp --root . --allow-write   # enables rewrite_apply
```

### Exit codes (ast-grep compatible)

| Command | 0                                                | 1                        | 2                                            |
| ------- | ------------------------------------------------ | ------------------------ | -------------------------------------------- |
| `run`   | at least one match                               | no matches               | runtime error (missing path, bad pattern, …) |
| `scan`  | no match at/above `--error-on` (default `error`) | match at/above threshold | runtime error (including zero rules loaded)  |

Stderr messages are prefixed with `ag4:`. Use `--debug` for stack traces.

### JSON output

`--json` / `--json=pretty|stream|compact` (default `pretty`). Fields align with ast-grep where practical:

- 0-based `range` line/column; text UI stays 1-based
- `byteOffset` (file encoding bytes, including BOM) and `charOffset` (UTF-16)
- `metaVariables.single` / `multi` / `transformed` with ranges
- `lines`, and on rewrite `replacement` / `replacementOffsets`
- `file` as a path relative to the current directory
- `encoding` when known

### Language ids

| Id       | Aliases            | Extensions                                                 |
| -------- | ------------------ | ---------------------------------------------------------- |
| `java`   |                    | `.java`                                                    |
| `cpp`    | `c++`, `cc`, `cxx` | `.cpp`, `.cc`, `.cxx`, `.c++`, `.hpp`, `.hh`, `.hxx`, `.h` |
| `csharp` | `cs`, `c#`         | `.cs`                                                      |
| `sqlite` |                    | `.sql`                                                     |

C++ uses the grammars-v4 **CPP14** grammar (MIT). C++17+ syntax and heavy macros may fail to parse; see [difftest/REPORT.md](difftest/REPORT.md).

C# uses **Roslyn** (`Microsoft.CodeAnalysis.CSharp`, MIT). Node kinds are Roslyn `SyntaxKind` names in snake_case (`invocation_expression`), with tree-sitter-c-sharp aliases where names differ (`identifier_name` ↔ `identifier`, binary ops ↔ `binary_expression`, …).

### Cursor MCP (`.cursor/mcp.json`)

```json
{
  "mcpServers": {
    "ag4": {
      "command": "ag4",
      "args": ["mcp", "--root", "."]
    }
  }
}
```

## Custom DSL

```yaml
# sgconfig.yml
encoding: utf-8
ruleDirs: [rules]
utilDirs: [utils]
customLanguages:
  calc:
    grammar: [grammars/CalcLexer.g4, grammars/CalcParser.g4]
    extensions: [calc]
    startRule: program
    patternRules: [statement, expr, program]
    metaVarVariants: ["{0}", "{0};"]
```

Requires Java + `tools/antlr-4.13.2-complete.jar` (lookup: `--antlr-jar`, `ANTLR_JAR`, `tools/` under sgconfig/cwd ancestors, then `BaseDirectory`), or prebuilt `.interp` files. See [samples/dsl](samples/dsl).

`run` / `debug` / `scan` accept `-c/--config`. Custom languages are registered lazily so `run -l java` does not compile unrelated grammars.

### Pattern tips

- Prefer concrete `patternRules` order (expressions/statements before the start rule).
- Multi-metavars in blocks/class bodies use language `metaVarVariants` (`{0}`, `{0};`, `int {0};`, …).
- Typed metavars: `${A:expression}` is checked with `KindMatches` at match time.

## Differential testing vs ast-grep

Compare ag4 against pinned `@ast-grep/cli@0.51.0` on real Java/C++/C# corpora:

```bash
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release -- --corpus difftest/corpus.yml
```

Clones go under `.difftest/` (gitignored). Summary: [difftest/REPORT.md](difftest/REPORT.md). CI: `.github/workflows/difftest.yml` (manual + weekly).

## Development

```bash
dotnet tool restore
npm ci
dotnet build AstGrepAntlr.slnx -c Release
dotnet test AstGrepAntlr.slnx -c Release
dotnet csharpier check .
npx prettier --check .
```

Requires JDK (for Antlr4BuildTasks) and .NET SDK 10 (see `global.json`). Known issues and fix status: [issue.md](issue.md).

## License

BSD-3-Clause. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
