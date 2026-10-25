# Vendored grammars

Grammars are copied from [antlr/grammars-v4](https://github.com/antlr/grammars-v4) at a fixed revision.
Each subdirectory contains its own `LICENSE`.

These `.g4` files are for the **ANTLR backend** (and for languages that are ANTLR-only: PL/SQL, SQLite, T-SQL, PostgreSQL, MySQL, COBOL, VB6, `customLanguages`).  
Java / C++ / Python **default to tree-sitter**, and JavaScript / TypeScript / Go / Rust / … are **tree-sitter only**; their tree-sitter grammars ship as native libraries from the [TreeSitter.DotNet](https://www.nuget.org/packages/TreeSitter.DotNet) NuGet package, not from this tree.

| Directory   | Language     | Status                                                      |
| ----------- | ------------ | ----------------------------------------------------------- |
| `java/`     | Java         | ANTLR backend parser (tree-sitter is default at runtime)    |
| `cpp/`      | C++14        | ANTLR backend parser (tree-sitter is default at runtime)    |
| `python/`   | Python 3     | ANTLR backend parser (tree-sitter is default at runtime)    |
| `plsql/`    | PL/SQL       | Built-in ANTLR parser                                       |
| `sqlite/`   | SQLite SQL   | Built-in ANTLR parser                                       |
| `tsql/`     | T-SQL        | Built-in ANTLR parser                                       |
| `postgresql/` | PostgreSQL | Built-in ANTLR parser                                       |
| `mysql/`    | MySQL        | Built-in ANTLR parser                                       |
| `cobol/`    | COBOL 85     | Built-in ANTLR parser (fixed format normalized in C#)       |
| `vb6/`      | VB6          | Built-in ANTLR parser                                       |
| (others)    | —            | Use `customLanguages` with `.g4` → `.interp`                |

Not vendored on purpose: grammars-v4 `fortran/*` (GPL-2.0), `vba/vba6` (GPL-3.0) and `vba/vba7_1` (no license
stated). They cannot be shipped in this BSD-3-Clause distribution; users may load their own copy via `customLanguages`.

Do not run CSharpier/Prettier on this tree.
