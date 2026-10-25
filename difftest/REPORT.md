# Difftest report (ast-grep vs ag4)

Generated (UTC): `2026-10-25T01:23–02:35Z` (expanded corpus + MetaVar / Smart / C# tree-sitter)  
Baseline tool: `@ast-grep/cli@0.51.0`  
Mode: `comparative`

```bash
# tree-sitter (default for java/cpp/python/csharp)
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release -- \
  --corpus difftest/corpus.yml --backend tree-sitter --skip-clone --skip-npm --skip-rewrite --timeout-seconds 90

# ANTLR (java/cpp/python)
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release -- \
  --corpus difftest/corpus.yml --backend antlr --skip-clone --skip-npm --skip-rewrite --timeout-seconds 180

# Roslyn (csharp only)
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release -- \
  --corpus difftest/corpus.yml --backend roslyn --language csharp --skip-clone --skip-npm --skip-rewrite
```

## Summary

### Java — tree-sitter (default)

| Metric                                    |  Value |
| ----------------------------------------- | -----: |
| Same                                      |  39521 |
| Only ast-grep                             |     68 |
| Only ag4                                  |    326 |
| Range diff                                |      0 |
| Metavar diff                              |    148 |
| Match rate                                | 98.65% |
| Unexplained (after known-differences.yml) |      0 |
| ast-grep time (ms)                        |  21849 |
| ag4 time (ms)                             |  95757 |
| Speed ratio (ag4 / ast-grep)              |  4.38x |

### Java — ANTLR

| Metric                                    |  Value |
| ----------------------------------------- | -----: |
| Same                                      |  30317 |
| Only ast-grep                             |   4705 |
| Only ag4                                  |  11162 |
| Range diff                                |      0 |
| Metavar diff                              |   4715 |
| Match rate                                | 59.56% |
| Unexplained (after known-differences.yml) |      0 |
| ast-grep time (ms)                        |  17690 |
| ag4 time (ms)                             | 368114 |
| Speed ratio (ag4 / ast-grep)              | 20.81x |

### C++ — tree-sitter (default)

| Metric                                    |  Value |
| ----------------------------------------- | -----: |
| Same                                      |  20459 |
| Only ast-grep                             |    162 |
| Only ag4                                  |    938 |
| Range diff                                |      0 |
| Metavar diff                              |    129 |
| Match rate                                | 94.33% |
| Unexplained (after known-differences.yml) |      0 |
| ast-grep time (ms)                        |  17415 |
| ag4 time (ms)                             |  77588 |
| Speed ratio (ag4 / ast-grep)              |  4.46x |

### C++ — ANTLR

| Metric                                    |   Value |
| ----------------------------------------- | ------: |
| Same                                      |    9396 |
| Only ast-grep                             |    9048 |
| Only ag4                                  |     819 |
| Range diff                                |       1 |
| Metavar diff                              |    2305 |
| Match rate                                |  43.56% |
| Unexplained (after known-differences.yml) |       0 |
| ast-grep time (ms)                        |   16340 |
| ag4 time (ms)                             | 2611307 |
| Speed ratio (ag4 / ast-grep)              | 159.81x |

Expanded `.cc` paths make ANTLR much slower; low match rate is still mostly **CPP14 limits**.

### C# — tree-sitter (default)

| Metric                                    |  Value |
| ----------------------------------------- | -----: |
| Same                                      | 101934 |
| Only ast-grep                             |  10936 |
| Only ag4                                  |   3200 |
| Range diff                                |      0 |
| Metavar diff                              |   8740 |
| Match rate                                | 81.67% |
| Unexplained (after known-differences.yml) |      0 |
| ast-grep time (ms)                        |  17856 |
| ag4 time (ms)                             |  98115 |
| Speed ratio (ag4 / ast-grep)              |  5.49x |

### C# — Roslyn (`--backend roslyn`)

| Metric                                    |  Value |
| ----------------------------------------- | -----: |
| Same                                      |  67525 |
| Only ast-grep                             |  47182 |
| Only ag4                                  |   5141 |
| Range diff                                |     19 |
| Metavar diff                              |   6884 |
| Match rate                                | 53.27% |
| Unexplained (after known-differences.yml) |      0 |
| ast-grep time (ms)                        |  17791 |
| ag4 time (ms)                             | 118047 |
| Speed ratio (ag4 / ast-grep)              |  6.63x |

### Python — tree-sitter (default)

| Metric                                    |  Value |
| ----------------------------------------- | -----: |
| Same                                      |  18010 |
| Only ast-grep                             |    491 |
| Only ag4                                  |     64 |
| Range diff                                |      0 |
| Metavar diff                              |    282 |
| Match rate                                | 95.56% |
| Unexplained (after known-differences.yml) |      0 |
| ast-grep time (ms)                        |  14786 |
| ag4 time (ms)                             |  73190 |
| Speed ratio (ag4 / ast-grep)              |  4.95x |

### Python — ANTLR

| Metric                                    |  Value |
| ----------------------------------------- | -----: |
| Same                                      |  10730 |
| Only ast-grep                             |   6332 |
| Only ag4                                  |    842 |
| Range diff                                |      0 |
| Metavar diff                              |   1721 |
| Match rate                                | 54.68% |
| Unexplained (after known-differences.yml) |      0 |
| ast-grep time (ms)                        |  14749 |
| ag4 time (ms)                             | 631639 |
| Speed ratio (ag4 / ast-grep)              | 42.83x |

### PL/SQL

Not compared: upstream `@ast-grep/cli` has no built-in PL/SQL language.

## Notes (this run)

- Corpus expanded: google-java-format, leveldb `.cc` paths, fmt `src`, flask, serilog; plus harder pattern shapes.
- **MetaVar JSON** now emits `$$$` as per-node `multi` arrays (ast-grep shape, including `,` tokens). Java MetaVarDiff **5580→148**.
- **Smart trailing skip** lets `if ($C) { $$$B }` match if-else (Java if-block OnlySg **328→0**).
- **C# default is tree-sitter**; Roslyn via `--backend roslyn`. Match rate TS **~82%** vs Roslyn **~53%**.
- Residuals catalogued under `backend: tree-sitter` (`ts-*-matcher`) / `backend: roslyn` (`csharp-*`); unexplained **0** for all measured backends.

## Configuration

- Patterns: [patterns/java.yml](patterns/java.yml), [patterns/cpp.yml](patterns/cpp.yml), [patterns/csharp.yml](patterns/csharp.yml), [patterns/python.yml](patterns/python.yml)
- Corpus: [corpus.yml](corpus.yml)
- Harness: `tools/AstGrepAntlr.DiffTest` vs `@ast-grep/cli@0.51.0`

## Artifacts / CI

- Machine-readable: `.difftest/report/latest/` (gitignored); per-backend copies under `.difftest/report/by-backend/`
- CI: [.github/workflows/difftest.yml](../.github/workflows/difftest.yml) — `workflow_dispatch` + weekly cron; fails on unexplained diffs
