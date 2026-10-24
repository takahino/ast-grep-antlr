# PL/SQL grammar (grammars-v4)

Vendored from [antlr/grammars-v4](https://github.com/antlr/grammars-v4) `sql/plsql/` directory.

- **Commit:** `77e908c3e76b0f4a352b0c5fe392606bfed1a4f1`
- **License:** Apache-2.0 — see [LICENSE](./LICENSE) and file headers in `PlSqlLexer.g4` / `PlSqlParser.g4`

Files:

- `PlSqlLexer.g4`, `PlSqlParser.g4` — ANTLR grammars (large)
- `CSharp/PlSqlLexerBase.cs`, `CSharp/PlSqlParserBase.cs` — C# bases (namespace adjusted)

Built-in language id: `plsql` (alias `pl/sql`). Extensions include `.pls`, `.pkb`, `.pks`, `.sql` (`.sql` also used by SQLite; choose with `-l`).
