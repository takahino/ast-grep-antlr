# Pattern syntax (ag4)

- `$A` — single named capture (metavariable)
- `$_` — anonymous single capture
- `$$$` / `$$$ARGS` — multi-node capture (lazy)
- `${A:expression}` — typed metavariable (ANTLR extension; uses rule bypass when available)

Strictness: `cst` | `smart` (default) | `ast` | `relaxed`

Patterns are parsed by trying each language `patternRules` entry until one consumes the fragment.
