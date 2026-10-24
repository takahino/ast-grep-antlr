# Difftest report (ast-grep vs ag4)

Generated (UTC): `2026-10-24T01:20:33Z` (Java match-compare; C++ section below)  
Baseline tool: `@ast-grep/cli@0.51.0`  
Mode: `comparative`  
Regenerate:

```bash
dotnet build AstGrepAntlr.slnx -c Release
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release --no-build -- \
  --corpus difftest/corpus.yml --patterns difftest/patterns/java.yml \
  --skip-clone --skip-npm --no-build --no-fail --skip-rewrite
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release --no-build -- \
  --corpus difftest/corpus.yml --patterns difftest/patterns/cpp.yml \
  --skip-clone --skip-npm --no-build --no-fail --skip-rewrite
```

## Summary (Java, post-fix)

| Metric                                    |  Value |
| ----------------------------------------- | -----: |
| Same                                      |  21393 |
| Only ast-grep                             |   4048 |
| Only ag4                                  |   6993 |
| Range diff                                |      0 |
| Metavar diff                              |   4573 |
| Match rate                                | 57.81% |
| Unexplained (after known-differences.yml) |      0 |
| ast-grep time (ms)                        |  10702 |
| ag4 time (ms)                             | 211108 |
| Speed ratio (ag4 / ast-grep)              | 19.73× |

## Baseline comparison (Phase 0b, pre-fix Java)

```
Totals: same=49172 only-ast-grep=14122 only-ag4=649 range-diff=3541 metavar-diff=17048
```

Notes vs Phase 0b:

- **range-diff 3541 → 0**: real file `byteOffset` / JSON alignment (Phase 2).
- **Pattern compile failures gone** for `if-block`, `for-loop`, `try-catch`, `public-method`, `while-loop`, `class-body` (Phase 4 + metavar hardening).
- **same count lower overall** mainly because JSON identity / metavar pairing is stricter and some broad patterns (`call-any`) now classify as only-ag4 / metavar-diff instead of same; quality of agreed matches improved (e.g. `if-block` same≈1949 vs 0 with errors before).
- Remaining only-\* / metavar-diff volumes are recorded in [known-differences.yml](known-differences.yml) as grammar / CST boundary (tree-sitter vs ANTLR).

## C++

Harness: `difftest/patterns/cpp.yml` × leveldb / cereal / fmt. Full matrix is intended for the weekly `difftest` workflow (ANTLR CPP14 matching is ~20–60s per pattern on leveldb).

Smoke sample (leveldb, first patterns, post-fix):

| Pattern        | same | only-sg | only-ag4 | meta | ag4 ms |
| -------------- | ---: | ------: | -------: | ---: | -----: |
| call-foo       |    0 |       0 |        0 |    0 |  13603 |
| call-any       | 2637 |    3615 |        0 |  990 |  25350 |
| qualified-call |    0 |       0 |      239 |    0 |  64514 |

`only-ast-grep` / `only-ag4` / parse failures on modern C++ are covered by `cpp-*` entries in [known-differences.yml](known-differences.yml).

## Repositories

See [corpus.yml](corpus.yml) (pinned tag + SHA).

## Artifacts

- Machine-readable: `.difftest/report/latest/details.json` (gitignored)
- CI: `.github/workflows/difftest.yml` (workflow_dispatch + weekly)
