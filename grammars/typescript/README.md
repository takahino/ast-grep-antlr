# typescript grammar

TypeScript is built in as `typescript` (alias `ts`) and `tsx` via **tree-sitter** (`tree-sitter-typescript`).
The ANTLR grammar is not compiled into the default build (size / base-class complexity).

To use the ANTLR grammar instead, use `customLanguages` in `sgconfig.yml` with grammars from
https://github.com/antlr/grammars-v4 and either:

1. Point `grammar` at `.g4` files (requires Java + `tools/antlr-4.13.2-complete.jar`), or
2. Point `grammar` at prebuilt `.interp` files.
