# PostgreSQL grammar (grammars-v4)

Vendored from [antlr/grammars-v4](https://github.com/antlr/grammars-v4) `sql/postgresql/` directory.

- **Commit:** `f02beda6df6a209440cc70b2630482851d610ca2`
- **License:** MIT — see [LICENSE](./LICENSE) and file headers; the grammar derives from PostgreSQL sources, see [COPYRIGHT](./COPYRIGHT) (PostgreSQL License)

Files:

- `PostgreSQLLexer.g4`, `PostgreSQLParser.g4` — ANTLR grammars (unmodified, large)
- `CSharp/*.cs` — C# lexer/parser bases and error listeners (namespace adjusted, `#nullable disable` added)

Built-in language id: `postgresql` (aliases `postgres`, `pgsql`). Extension: `.sql` (shared with other SQL dialects; choose with `-l`).
