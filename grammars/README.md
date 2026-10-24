# Vendored grammars

Grammars are copied from [antlr/grammars-v4](https://github.com/antlr/grammars-v4) at a fixed revision.
Each subdirectory contains its own `LICENSE`.

| Directory   | Language     | Status                                      |
| ----------- | ------------ | ------------------------------------------- |
| `java/`     | Java         | Built-in generated parser                   |
| `cpp/`      | C++14        | Built-in generated parser                   |
| `python/`   | Python 3     | Built-in generated parser                   |
| `plsql/`    | PL/SQL       | Built-in generated parser                   |
| `sqlite/`   | SQLite SQL   | Built-in generated parser                   |
| (others)    | js/ts/…      | Use `customLanguages` with `.g4` → `.interp` |

Do not run CSharpier/Prettier on this tree.
