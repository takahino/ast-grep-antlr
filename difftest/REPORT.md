# Difftest report (ast-grep vs ag4)

Generated (UTC): `2026-10-25T04:46–05:42Z` (after speedup work; match counts unchanged vs prior baseline)  
Baseline tool: `@ast-grep/cli@0.51.0`  
Mode: `comparative`

```bash
# tree-sitter (default for java/cpp/python/csharp)
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release -- \
  --corpus difftest/corpus.yml --backend tree-sitter --skip-clone --skip-npm --no-fail

# ANTLR (java/cpp/python; csharp stays tree-sitter)
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release -- \
  --corpus difftest/corpus.yml --backend antlr --skip-clone --skip-npm --no-fail

# Roslyn (csharp only)
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release -- \
  --corpus difftest/corpus.yml --backend roslyn --language csharp --skip-clone --skip-npm --no-fail
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
| ast-grep time (ms)                        |  20085 |
| ag4 time (ms)                             |  83265 |
| Speed ratio (ag4 / ast-grep)              |  4.15x |

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
| ast-grep time (ms)                        |  19338 |
| ag4 time (ms)                             | 381659 |
| Speed ratio (ag4 / ast-grep)              | 19.74x |

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
| ast-grep time (ms)                        |  17505 |
| ag4 time (ms)                             |  73706 |
| Speed ratio (ag4 / ast-grep)              |  4.21x |

### C++ — ANTLR

| Metric                                    |   Value |
| ----------------------------------------- | ------: |
| Same                                      |    9396 |
| Only ast-grep                             |    9048 |
| Only ag4                                  |     775 |
| Range diff                                |       1 |
| Metavar diff                              |    2305 |
| Match rate                                |  43.65% |
| Unexplained (after known-differences.yml) |       0 |
| ast-grep time (ms)                        |   17851 |
| ag4 time (ms)                             | 2104623 |
| Speed ratio (ag4 / ast-grep)              | 117.90x |

Expanded `.cc` paths make ANTLR much slower; low match rate is still mostly **CPP14 limits**. Bench: **75% LL fallback** on leveldb sample.

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
| ast-grep time (ms)                        |  18415 |
| ag4 time (ms)                             |  98597 |
| Speed ratio (ag4 / ast-grep)              |  5.35x |

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
| ast-grep time (ms)                        |  19054 |
| ag4 time (ms)                             | 116774 |
| Speed ratio (ag4 / ast-grep)              |  6.13x |

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
| ast-grep time (ms)                        |  13860 |
| ag4 time (ms)                             |  64211 |
| Speed ratio (ag4 / ast-grep)              |  4.63x |

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
| ast-grep time (ms)                        |  14398 |
| ag4 time (ms)                             | 535414 |
| Speed ratio (ag4 / ast-grep)              | 37.19x |

### PL/SQL

Not compared: upstream `@ast-grep/cli` has no built-in PL/SQL language.

## Notes (this run)

- Match counts equal the pre-speedup baseline (tree-sitter / antlr / roslyn). Wall times improved modestly on TS paths.
- **Bench** (`tools/AstGrepAntlr.Bench`): parse-phase timings + LL fallback rate. Java ANTLR **67%** LL fallback on commons-lang; C++ ANTLR **75%** on leveldb sample. See [speedupissue.md](../speedupissue.md) and [ast-grep-vs-ag4.html](../ast-grep-vs-ag4.html) §2.2.
- Speedups landed: tree-sitter cursor / LibraryImport / `has_error` prune; ANTLR Build-once + cancelable timeout; lazy language registration; matcher undo-log / MaxResults early stop.
- Residuals remain catalogued in `known-differences.yml`; unexplained **0**.

## Configuration

- Patterns: [patterns/java.yml](patterns/java.yml), [patterns/cpp.yml](patterns/cpp.yml), [patterns/csharp.yml](patterns/csharp.yml), [patterns/python.yml](patterns/python.yml)
- Corpus: [corpus.yml](corpus.yml)
- Harness: `tools/AstGrepAntlr.DiffTest` vs `@ast-grep/cli@0.51.0`

## Artifacts / CI

- Machine-readable: `.difftest/report/latest/` (gitignored); speedup snapshot under `.difftest/report/after/`
- CI: [.github/workflows/difftest.yml](../.github/workflows/difftest.yml) — `workflow_dispatch` + weekly cron; fails on unexplained diffs
