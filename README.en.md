# ast-grep-antlr (`ag4`)

English | [日本語](README.md)

**Unofficial** structural search tool inspired by [ast-grep](https://ast-grep.github.io/). Parsers are chosen per language:

- **Java / C++ / Python / C#** — [tree-sitter](https://tree-sitter.github.io/) (default) for speed and upstream-compatible kind names
- **PL/SQL / SQLite / custom DSLs** — [ANTLR4](https://www.antlr.org/) (`.g4` / `.interp`)
- **C# (optional)** — [Roslyn](https://github.com/dotnet/roslyn) (`--backend roslyn`)

This project is **not** affiliated with the official ast-grep project.

Internals (pipeline, parsing, matching, rule engine, hosts): [architecture.html](architecture.html) (Japanese).

## How it differs from ast-grep

ag4 keeps ast-grep's workflow (patterns, YAML rules, CLI, JSON output, exit codes) and aims to bring structural search to languages and environments that tree-sitter alone does not reach.

- **Per-language parser choice** — switch between tree-sitter / ANTLR4 / Roslyn with the same patterns and rules
- **Add languages with `.g4`** — ships PL/SQL and SQLite (not available upstream); in-house DSLs work by adding a grammar to `customLanguages`
- **Embeddable in .NET / native code** — in-process `Searcher` and the NativeAOT shared library `ag4_native` (C ABI)
- **Legacy encodings** — reads Shift_JIS / EUC-JP and more, and preserves the original encoding on rewrite
- **Continuously diffed against ast-grep** — differential tests on real OSS corpora; remaining differences are documented with reasons in `known-differences.yml`

On the other hand, ag4 is slower than ast-grep and does not aim for hit sets identical to upstream. See [ast-grep-vs-ag4.en.html](ast-grep-vs-ag4.en.html) for numbers.

## Features

- Metavariable patterns: `$A`, `$_`, `$$$ARGS`, typed `${A:expression}` (supertype / rule name)
- YAML rules: `pattern`, `kind`, `inside`, `has`, `precedes`, `follows`, `all`/`any`/`not`, `constraints`, `matches`, `fix`, `files`/`ignores`
- `stopBy`: `neighbor` (default, ast-grep-compatible), `end`, or a kind name
- Backend switch: `--backend tree-sitter|antlr|roslyn`, `languageBackends` in `sgconfig.yml`
- Continues after syntax errors (tree-sitter ERROR/MISSING; ANTLR SLL → LL + recovery)
- Parallel file walk with literal prefilter, gitignore, and `--globs`
- Encoding preservation (UTF-8/BOM, UTF-16/32, Shift_JIS, EUC-JP) via `--encoding` / `sgconfig.yml`
- Custom DSLs via `.g4` / `.interp` (`sgconfig.yml` → `customLanguages`, lazy-loaded)
- MCP server: `ag4 mcp` (stdio) for AI agents
- Built-in languages: **Java**, **C++**, **C#** (tree-sitter default / Roslyn opt-in), **Python** 3, **PL/SQL**, **SQLite** (more via custom grammars)

## Install

```bash
# from source
dotnet tool install -g AstGrepAntlr.Cli --add-source ./artifacts/nupkg

# or run
dotnet run --project src/AstGrepAntlr.Cli -- run -p 'System.out.println($A)' -l java src/
```

NativeAOT binaries are published on GitHub Releases (`win-x64` / `linux-x64` / `osx-arm64`). tree-sitter native libraries ship with the release (usually under `runtimes/<rid>/native/`).

## Quick start

```bash
ag4 run -p 'System.out.println($A)' -l java path/to/src
ag4 run -p 'System.out.println($A)' -l java --backend antlr path/to/src
ag4 run -p 'System.out.println($A)' -l java --json path/to/src
ag4 run -p 'foo($A)' -l java --strictness smart --max-results 20 --globs '**/*.java' src/
ag4 run -p 'foo($A)' -l java --rewrite 'bar($A)' --apply src/
ag4 scan -r rules/no-println.yml path/to/src
ag4 scan -c sgconfig.yml --error-on warning path/to/src
ag4 debug parse -l java --code 'class A {}'
ag4 debug pattern -l java -p 'System.out.println($A)'
ag4 version
ag4 mcp --root .
ag4 mcp --root . --allow-write
```

### Exit codes (ast-grep compatible)

| Command | 0                                                | 1                        | 2                                 |
| ------- | ------------------------------------------------ | ------------------------ | --------------------------------- |
| `run`   | ≥1 match                                         | no matches               | runtime error                     |
| `scan`  | no match at/above `--error-on` (default `error`) | match at/above threshold | runtime error (including 0 rules) |

stderr lines are prefixed with `ag4:`. Stack traces require `--debug`.

### JSON output

`--json` / `--json=pretty|stream|compact` (default `pretty`). Fields align with ast-grep where practical: 0-based `range`, `byteOffset` / `charOffset`, `metaVariables`, `lines`, rewrite fields, relative `file`, optional `encoding`.

### Language ids

| Id       | Aliases            | Extensions                     | Default backend | Switch                    |
| -------- | ------------------ | ------------------------------ | --------------- | ------------------------- |
| `java`   |                    | `.java`                        | tree-sitter     | `--backend antlr`         |
| `cpp`    | `c++`, `cc`, `cxx` | `.cpp`, `.cc`, …               | tree-sitter     | `--backend antlr` (CPP14) |
| `csharp` | `cs`, `c#`         | `.cs`                          | tree-sitter     | `--backend roslyn`        |
| `python` | `py`               | `.py`                          | tree-sitter     | `--backend antlr`         |
| `plsql`  | `pl/sql`           | `.pls`, `.pkb`, `.pks`, `.sql` | ANTLR           | —                         |
| `sqlite` |                    | `.sql`, `.sqlite`              | ANTLR           | —                         |

With tree-sitter, kind names match upstream ast-grep (`method_invocation`, `class_declaration`, …). With `--backend antlr`, kinds follow grammars-v4 (`methodCall`, `compilationunit`, …).

## Choosing a backend

```bash
ag4 run -l java --backend tree-sitter -p '$F($$$ARGS)' src/
ag4 run -l java --backend antlr -p '$F($$$ARGS)' src/
ag4 run -l csharp --backend roslyn -p '$F($$$ARGS)' src/
```

```yaml
# sgconfig.yml
languageBackends:
  java: antlr
  csharp: roslyn
  python: tree-sitter
```

CLI `--backend` overrides sgconfig and applies only to languages that support that backend (e.g. `--backend antlr` leaves csharp on tree-sitter). If tree-sitter natives cannot be loaded, ag4 warns on stderr and falls back to Roslyn for csharp / ANTLR otherwise.

### Kind migration (ANTLR → tree-sitter)

| ANTLR (examples)    | tree-sitter (examples) |
| ------------------- | ---------------------- |
| `methodDeclaration` | `method_declaration`   |
| `methodCall`        | `method_invocation`    |
| `compilationunit`   | `program`              |

`${A:expression}` still works via tree-sitter supertype aliases. Update `kind:` in rules or keep `languageBackends: { java: antlr }` temporarily.

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

## Custom DSLs

```yaml
customLanguages:
  calc:
    grammar: [grammars/CalcLexer.g4, grammars/CalcParser.g4]
    extensions: [calc]
    startRule: program
    patternRules: [statement, expr, program]
    metaVarVariants: ["{0}", "{0};"]
```

Requires a JDK and `tools/antlr-4.13.2-complete.jar` (or `--antlr-jar` / `ANTLR_JAR`). See [samples/dsl](samples/dsl). Custom languages are registered lazily.

## Embedding / sample GUIs

NativeAOT shared library `ag4_native` exposes a small C ABI (`ag4_search` JSON in/out; optional `backend`). Samples: [samples/gui/README.md](samples/gui/README.md) (place tree-sitter natives next to `ag4_native`). Header: [samples/gui/include/ag4_native.h](samples/gui/include/ag4_native.h).

In-process .NET: reference `AstGrepAntlr.Core` + `AstGrepAntlr.Languages` and call `Searcher`.

ANTLR backends warm their prediction DFAs across repeated parses in the same process. Prefer **DLL / NuGet (`Searcher`) / MCP** over one-shot CLI invocations when you care about parse speed.

## Differential tests vs ast-grep

```bash
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release -- --corpus difftest/corpus.yml
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release -- --corpus difftest/corpus.yml --backend antlr
```

See [difftest/REPORT.md](difftest/REPORT.md) and [ast-grep-vs-ag4.en.html](ast-grep-vs-ag4.en.html). Micro-benchmark: `tools/AstGrepAntlr.Bench`. Script: [bench/compare-ast-grep.ps1](bench/compare-ast-grep.ps1) (`-Backend`, prefers a published `ag4`).

## Development

```bash
dotnet tool restore
npm ci
dotnet build AstGrepAntlr.slnx -c Release
dotnet test AstGrepAntlr.slnx -c Release
dotnet csharpier check .
npx prettier --check .
```

Needs JDK (Antlr4BuildTasks) and .NET SDK 10. tree-sitter natives come prebuilt from NuGet (TreeSitter.DotNet) — **no C toolchain required**.

## License

BSD-3-Clause. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
