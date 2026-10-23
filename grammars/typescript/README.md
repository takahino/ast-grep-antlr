# typescript grammar

Not compiled into the default build (size / base-class complexity).

Use `customLanguages` in `sgconfig.yml` with grammars from
https://github.com/antlr/grammars-v4 and either:

1. Point `grammar` at `.g4` files (requires Java + `tools/antlr-4.13.2-complete.jar`), or
2. Point `grammar` at prebuilt `.interp` files.

Built-in generated support may be added in a later release.
