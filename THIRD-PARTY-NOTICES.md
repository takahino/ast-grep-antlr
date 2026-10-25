# Third-party notices

## ANTLR 4 runtime and tool

- Package: `Antlr4.Runtime.Standard`, ANTLR tool jar
- License: BSD-3-Clause
- https://www.antlr.org/

## grammars-v4 (vendored)

| Path               | License      | Notes                         |
| ------------------ | ------------ | ----------------------------- |
| `grammars/java/`   | BSD-3-Clause | See `grammars/java/LICENSE`   |
| `grammars/sqlite/` | MIT          | See `grammars/sqlite/LICENSE` |
| `grammars/cpp/`    | MIT          | See `grammars/cpp/LICENSE`    |
| `grammars/python/` | MIT          | See `grammars/python/LICENSE` |
| `grammars/plsql/`  | Apache-2.0   | See `grammars/plsql/LICENSE`  |

Source: https://github.com/antlr/grammars-v4

These `.g4` trees are used by the **ANTLR** backend. tree-sitter grammars (Java / C++ / Python / C# and the tree-sitter-only languages) come from NuGet natives (below), not from this directory.

## tree-sitter (native)

| Component                                 | License | Notes                                           |
| ----------------------------------------- | ------- | ----------------------------------------------- |
| tree-sitter                               | MIT     | Core library via TreeSitter.DotNet natives      |
| tree-sitter-java                          | MIT     | Bundled grammar                                 |
| tree-sitter-cpp                           | MIT     | Bundled grammar                                 |
| tree-sitter-python                        | MIT     | Bundled grammar                                 |
| tree-sitter-c-sharp                       | MIT     | Bundled grammar                                 |
| tree-sitter-javascript                    | MIT     | Bundled grammar                                 |
| tree-sitter-typescript (typescript / tsx) | MIT     | Bundled grammar                                 |
| tree-sitter-go                            | MIT     | Bundled grammar                                 |
| tree-sitter-rust                          | MIT     | Bundled grammar                                 |
| tree-sitter-c                             | MIT     | Bundled grammar                                 |
| tree-sitter-ruby                          | MIT     | Bundled grammar                                 |
| tree-sitter-php                           | MIT     | Bundled grammar                                 |
| tree-sitter-bash                          | MIT     | Bundled grammar                                 |
| tree-sitter-html                          | MIT     | Bundled grammar                                 |
| tree-sitter-css                           | MIT     | Bundled grammar                                 |
| tree-sitter-json                          | MIT     | Bundled grammar                                 |
| tree-sitter-scala                         | MIT     | Bundled grammar                                 |
| tree-sitter-haskell                       | MIT     | Bundled grammar                                 |
| tree-sitter-julia                         | MIT     | Bundled grammar                                 |
| tree-sitter-ocaml                         | MIT     | Bundled grammar                                 |
| tree-sitter-toml                          | MIT     | Bundled grammar                                 |
| tree-sitter-verilog                       | MIT     | Bundled grammar                                 |
| TreeSitter.DotNet                         | MIT     | NuGet: `runtimes/<rid>/native/` (win/linux/osx) |

Bindings are custom `LibraryImport` (`TsNative`); unused grammar natives are filtered from publish output.

## NuGet dependencies

- YamlDotNet — MIT
- System.CommandLine — MIT
- ModelContextProtocol — MIT
- Microsoft.Extensions.\* — MIT
- Microsoft.CodeAnalysis.CSharp (Roslyn) — MIT
- TreeSitter.DotNet — MIT (natives; see above)
- xunit / test SDK — Apache-2.0 / MIT

## Dev tooling

- CSharpier — MIT
- Prettier — MIT
