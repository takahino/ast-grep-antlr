# Pattern syntax (ag4)

- `$A` — single named capture (metavariable)
- `$_` — anonymous single capture
- `$$$` / `$$$ARGS` — multi-node capture (lazy)
- `${A:expression}` — typed metavariable (matches only if the capture’s kind is `expression` or an alias / supertype of it)

Strictness: `cst` | `smart` (default) | `ast` | `relaxed`

Patterns are parsed by trying each language pattern entry until one consumes the fragment.

- **tree-sitter**: wrap the fragment in a language-specific context template, then extract the node whose range matches the embedded span
- **ANTLR**: try each `patternRules` / start-rule entry
- Kind names differ by backend; prefer tree-sitter kinds when using the default for Java / C++ / Python
