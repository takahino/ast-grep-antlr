# ast-grep-antlr (`ag4`)

**Unofficial** structural search tool inspired by [ast-grep](https://ast-grep.github.io/), powered by [ANTLR4](https://www.antlr.org/) instead of tree-sitter.

This project is **not** affiliated with the official ast-grep project.

## Features

- Metavariable patterns: `$A`, `$_`, `$$$ARGS`, plus ANTLR extension `${A:expression}`
- YAML rules compatible with a useful subset of ast-grep (`pattern`, `kind`, `inside`, `has`, `all`/`any`/`not`, `constraints`, `fix`)
- Continues after syntax errors (SLL → LL + error recovery)
- Parallel file walk with literal prefilter
- Custom DSLs via `.g4` / `.interp` (`sgconfig.yml` → `customLanguages`)
- MCP server: `ag4 mcp` (stdio) for AI agents
- Built-in languages: **Java**, **SQLite** (more via custom grammars)

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
ag4 scan -r rules/no-println.yml path/to/src
ag4 debug parse -l java --code 'class A {}'
ag4 debug pattern -l java -p 'System.out.println($A)'
ag4 mcp --root .                 # read-only MCP
ag4 mcp --root . --allow-write   # enables rewrite_apply
```

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
customLanguages:
  calc:
    grammar: [grammars/CalcLexer.g4, grammars/CalcParser.g4]
    extensions: [calc]
    startRule: program
    patternRules: [program, statement, expr]
```

Requires Java + `tools/antlr-4.13.2-complete.jar` (or prebuilt `.interp` files). See [samples/dsl](samples/dsl).

## Development

```bash
dotnet tool restore
npm ci
dotnet build AstGrepAntlr.slnx -c Release
dotnet test AstGrepAntlr.slnx -c Release
dotnet csharpier check .
npx prettier --check .
```

Requires JDK (for Antlr4BuildTasks) and .NET SDK 10 (see `global.json`).

## License

BSD-3-Clause. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

English: [README.en.md](README.en.md)
