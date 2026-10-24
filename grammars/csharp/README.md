# csharp grammar

Built-in C# support uses **Roslyn** (`Microsoft.CodeAnalysis.CSharp`, MIT), not
the grammars-v4 ANTLR grammar (EPL-1.0). Language id: `csharp` (aliases `cs`, `c#`).

You can still load an ANTLR C# grammar via `customLanguages` in `sgconfig.yml`
from https://github.com/antlr/grammars-v4 (`.g4` or prebuilt `.interp`), under a
different language id if desired.

See the main [README](../../README.md) for kind naming (Roslyn snake_case +
tree-sitter-c-sharp aliases) and [difftest/REPORT.md](../../difftest/REPORT.md)
for comparison numbers vs ast-grep.
