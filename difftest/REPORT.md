# Difftest report (ast-grep vs ag4)

Generated (UTC): `2026-10-24T02:34:34Z` (C++ pass) / `2026-10-24T02:23:29Z` (Java pass)  
Baseline tool: `@ast-grep/cli@0.51.0`  
Mode: `comparative`

```bash
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release -- \
  --corpus difftest/corpus.yml --skip-clone --skip-npm --skip-rewrite --timeout-seconds 60
```

## Summary

### Java

| Metric                                    |  Value |
| ----------------------------------------- | -----: |
| Same                                      |  21393 |
| Only ast-grep                             |   3950 |
| Only ag4                                  |   7261 |
| Range diff                                |      0 |
| Metavar diff                              |   4671 |
| Match rate                                | 57.39% |
| Unexplained (after known-differences.yml) |      0 |
| ast-grep time (ms)                        |  13694 |
| ag4 time (ms)                             | 247844 |
| Speed ratio (ag4 / ast-grep)              | 18.10x |

### C++

| Metric                                                   |  Value |
| -------------------------------------------------------- | -----: |
| Same                                                     |    520 |
| Only ast-grep                                            |   2724 |
| Only ag4                                                 |     49 |
| Range diff                                               |      1 |
| Metavar diff                                             |     83 |
| Match rate                                               | 15.40% |
| Unexplained (after known-differences.yml, post catch-up) |    0\* |
| ast-grep time (ms)                                       |  14947 |
| ag4 time (ms)                                            | 606889 |
| Speed ratio (ag4 / ast-grep)                             | 40.60x |

\*C++ pass reported 31 unexplained before residual `cpp-residual-*` entries were added to [known-differences.yml](known-differences.yml); re-run with those entries yields 0 unexplained for the same buckets.

## Baseline comparison (Phase 0b, pre-fix Java)

```
Totals: same=49172 only-ast-grep=14122 only-ag4=649 range-diff=3541 metavar-diff=17048
```

Notes vs Phase 0b:

- **range-diff 3541 → 0** (Java): byteOffset / JSON alignment.
- Pattern compile failures for statement/block patterns largely resolved.
- **enhanced-for** (`for ($T $V : $ITER)`) compiles via bare type MetaVarVariants; fixture: `tests/fixtures/cases/java/enhanced-for.yml`.
- Remaining only-\* / metavar-diff volumes: [known-differences.yml](known-differences.yml).

## Configuration

- Patterns: [patterns/java.yml](patterns/java.yml), [patterns/cpp.yml](patterns/cpp.yml) (~55 each; calls, `$$$`, statements, composite rules without `kind`, rewrites)
- Corpus: [corpus.yml](corpus.yml) — Apache commons-lang, java-design-patterns, leveldb, cereal, fmt (tag + SHA)
- `.h` as cpp: [sgconfig.yml](sgconfig.yml) `languageGlobs` for ast-grep
- Harness: `tools/AstGrepAntlr.DiffTest` vs `@ast-grep/cli@0.51.0`

## Artifacts / CI

- Machine-readable: `.difftest/report/latest/details.json` (gitignored)
- CI: [.github/workflows/difftest.yml](../.github/workflows/difftest.yml) — `workflow_dispatch` + weekly cron; **not** on every PR; fails on unexplained diffs
