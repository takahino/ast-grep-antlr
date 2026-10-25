# Difftest report (ast-grep vs ag4)

Generated (UTC): `2026-10-24T18:10Z` (Java/C++/Python tree-sitter, Only\* bugfixes) / `2026-10-24T16:21–16:57Z` (ANTLR) / `2026-10-24T16:17Z` (C#)  
Baseline tool: `@ast-grep/cli@0.51.0`  
Mode: `comparative`

```bash
# tree-sitter (default for java/cpp/python)
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release -- \
  --corpus difftest/corpus.yml --backend tree-sitter --skip-clone --skip-npm --skip-rewrite --timeout-seconds 90

# ANTLR (opt-in)
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release -- \
  --corpus difftest/corpus.yml --backend antlr --skip-clone --skip-npm --skip-rewrite --timeout-seconds 90
```

C# is always Roslyn (no `--backend` effect).

## Summary

### Java — tree-sitter (default)

| Metric                                    |  Value |
| ----------------------------------------- | -----: |
| Same                                      |  24088 |
| Only ast-grep                             |    346 |
| Only ag4                                  |    157 |
| Range diff                                |      0 |
| Metavar diff                              |   5580 |
| Match rate                                | 79.84% |
| Unexplained (after known-differences.yml) |      0 |
| ast-grep time (ms)                        |  10745 |
| ag4 time (ms)                             |  48992 |
| Speed ratio (ag4 / ast-grep)              |  4.56x |

### Java — ANTLR

| Metric                                    |  Value |
| ----------------------------------------- | -----: |
| Same                                      |  21393 |
| Only ast-grep                             |   3950 |
| Only ag4                                  |   7261 |
| Range diff                                |      0 |
| Metavar diff                              |   4671 |
| Match rate                                | 57.39% |
| Unexplained (after known-differences.yml) |      0 |
| ast-grep time (ms)                        |  10792 |
| ag4 time (ms)                             | 216441 |
| Speed ratio (ag4 / ast-grep)              | 20.06x |

### C++ — tree-sitter (default)

| Metric                                    |  Value |
| ----------------------------------------- | -----: |
| Same                                      |   2729 |
| Only ast-grep                             |     56 |
| Only ag4                                  |    227 |
| Range diff                                |      0 |
| Metavar diff                              |    543 |
| Match rate                                | 76.77% |
| Unexplained (after known-differences.yml) |      0 |
| ast-grep time (ms)                        |  10831 |
| ag4 time (ms)                             |  49636 |
| Speed ratio (ag4 / ast-grep)              |  4.58x |

### C++ — ANTLR

| Metric                                    |  Value |
| ----------------------------------------- | -----: |
| Same                                      |    520 |
| Only ast-grep                             |   2724 |
| Only ag4                                  |     49 |
| Range diff                                |      1 |
| Metavar diff                              |     83 |
| Match rate                                | 15.40% |
| Unexplained (after known-differences.yml) |      0 |
| ast-grep time (ms)                        |  13117 |
| ag4 time (ms)                             | 831937 |
| Speed ratio (ag4 / ast-grep)              | 63.42x |

Low ANTLR match rate is mostly **CPP14 grammar limits** (C++17+, macros).

### C# (Roslyn)

| Metric                                    |  Value |
| ----------------------------------------- | -----: |
| Same                                      |  50540 |
| Only ast-grep                             |  39042 |
| Only ag4                                  |   3736 |
| Range diff                                |     13 |
| Metavar diff                              |   9423 |
| Match rate                                | 49.19% |
| Unexplained (after known-differences.yml) |      0 |
| ast-grep time (ms)                        |  12527 |
| ag4 time (ms)                             |  70577 |
| Speed ratio (ag4 / ast-grep)              |  5.63x |

### Python — tree-sitter (default)

| Metric                                    |  Value |
| ----------------------------------------- | -----: |
| Same                                      |   9774 |
| Only ast-grep                             |    283 |
| Only ag4                                  |     45 |
| Range diff                                |      0 |
| Metavar diff                              |   1570 |
| Match rate                                | 83.74% |
| Unexplained (after known-differences.yml) |      0 |
| ast-grep time (ms)                        |   7134 |
| ag4 time (ms)                             |  32285 |
| Speed ratio (ag4 / ast-grep)              |  4.53x |

### Python — ANTLR

| Metric                                    |  Value |
| ----------------------------------------- | -----: |
| Same                                      |   6938 |
| Only ast-grep                             |   3679 |
| Only ag4                                  |     42 |
| Range diff                                |      0 |
| Metavar diff                              |   1010 |
| Match rate                                | 59.46% |
| Unexplained (after known-differences.yml) |      0 |
| ast-grep time (ms)                        |  10122 |
| ag4 time (ms)                             | 469568 |
| Speed ratio (ag4 / ast-grep)              | 46.39x |

### PL/SQL

Not compared: upstream `@ast-grep/cli` has no built-in PL/SQL language.

## Notes (hybrid backends / Only\* fixes)

- Default for Java / C++ / Python is **tree-sitter**.
- Versus ANTLR: match rate **Java 57%→80%**, **C++ 15%→77%**, **Python 59%→84%**; speed ratio **~20×→5×**, **~63×→5×**, **~46×→5×**.
- Bugfixes that removed large OnlyAstGrep buckets:
  - Expression-context false accepts for statement fragments (`$VAR = $VAL;`, `break;`, …)
  - Python `dotted_name` excluded from B6 single-child list guard
  - C++ `#include <$H>` metavar marking for `<…>` / `"…"` path tokens
  - DiffTest passes `-p=@Override` as `-p=…` so the dotnet host does not treat `@` as a response file
- Remaining Only\* are mostly intentional matcher differences (`enhanced-for`, `qualified-call`, residual `if-block`) plus MetaVarDiff; catalogued under `backend: tree-sitter` (`ts-*-matcher`).
- Parse vs match cost breakdown: `tools/AstGrepAntlr.Bench`.

## Configuration

- Patterns: [patterns/java.yml](patterns/java.yml), [patterns/cpp.yml](patterns/cpp.yml), [patterns/csharp.yml](patterns/csharp.yml), [patterns/python.yml](patterns/python.yml)
- Corpus: [corpus.yml](corpus.yml)
- Harness: `tools/AstGrepAntlr.DiffTest` vs `@ast-grep/cli@0.51.0` (`--backend tree-sitter|antlr`)

## Artifacts / CI

- Machine-readable: `.difftest/report/latest/` (gitignored); per-backend copies under `.difftest/report/by-backend/`
- CI: [.github/workflows/difftest.yml](../.github/workflows/difftest.yml) — `workflow_dispatch` + weekly cron; fails on unexplained diffs
