# COBOL 85 grammar (grammars-v4)

Vendored from [antlr/grammars-v4](https://github.com/antlr/grammars-v4) `cobol85/` directory.

- **Commit:** `f02beda6df6a209440cc70b2630482851d610ca2`
- **License:** MIT — see [LICENSE](./LICENSE) and the header of `Cobol85.g4`

Files:

- `Cobol85.g4` — combined ANTLR grammar (unmodified)

Built-in language id: `cobol` (alias `cobol85`). Extensions: `.cbl`, `.cob`, `.cpy`.

Upstream expects input that has already gone through `Cobol85Preprocessor.g4`. ag4 does not vendor the
preprocessor; instead `CobolLanguage` blanks the sequence area (columns 1–6), the indicator (column 7),
comment lines (`*` / `/`) and columns 73+ of reference-format sources before lexing. COPY / REPLACE are
not expanded, and continuation lines (`-` in column 7) are not joined.
