# Vendored grammars

Grammars are copied from [antlr/grammars-v4](https://github.com/antlr/grammars-v4) at a fixed revision.
Each subdirectory contains its own `LICENSE`.

These `.g4` files are for the **ANTLR backend** (and for languages that are ANTLR-only: PL/SQL, SQLite, `customLanguages`).  
Java / C++ / Python **default to tree-sitter**, and JavaScript / TypeScript / Go / Rust / … are **tree-sitter only**; their tree-sitter grammars ship as native libraries from the [TreeSitter.DotNet](https://www.nuget.org/packages/TreeSitter.DotNet) NuGet package, not from this tree.

| Directory   | Language     | Status                                                      |
| ----------- | ------------ | ----------------------------------------------------------- |
| `java/`     | Java         | ANTLR backend parser (tree-sitter is default at runtime)    |
| `cpp/`      | C++14        | ANTLR backend parser (tree-sitter is default at runtime)    |
| `python/`   | Python 3     | ANTLR backend parser (tree-sitter is default at runtime)    |
| `plsql/`    | PL/SQL       | Built-in ANTLR parser                                       |
| `sqlite/`   | SQLite SQL   | Built-in ANTLR parser                                       |
| (others)    | —            | Use `customLanguages` with `.g4` → `.interp`                |

Do not run CSharpier/Prettier on this tree.
