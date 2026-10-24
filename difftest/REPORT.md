# Differential test report (ast-grep vs ag4)

Generated from the `fix/issue-backlog` work. Regenerate with:

```bash
dotnet build AstGrepAntlr.slnx -c Release
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release --no-build -- \
  --corpus difftest/corpus.yml --patterns difftest/patterns/java.yml
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release --no-build -- \
  --corpus difftest/corpus.yml --patterns difftest/patterns/cpp.yml --skip-clone --skip-npm
```

Pinned tools: `@ast-grep/cli@0.51.0` (npm prefix under `.difftest/tools`).  
Corpora: see [corpus.yml](corpus.yml). Known grammar diffs: [known-differences.yml](known-differences.yml).

## Summary (latest local/CI run)

| Language |                                                 Same | Only ast-grep | Only ag4 | Range diff | MetaVar diff | Notes                                      |
| -------- | ---------------------------------------------------: | ------------: | -------: | ---------: | -----------: | ------------------------------------------ |
| Java     | _(fill from `.difftest/report/baseline/summary.md`)_ |               |          |            |              | commons-lang + java-design-patterns        |
| C++      |                                             _(fill)_ |               |          |            |              | leveldb + cereal; fmt for parse-error rate |

Machine-readable details: `.difftest/report/baseline/details.json` (gitignored artifact).

## Baseline before fixes

An early Java-only baseline was captured during Phase 0b under `.difftest/report/baseline/` before encoding/pattern/CLI fixes. Compare new runs against that directory when present.

## Residual known differences

See [known-differences.yml](known-differences.yml). Unexplained diffs (not listed there) should fail the weekly `difftest` workflow after triage.

## Timing

Record wall-clock per pattern in `details.json` (`AstGrepMs` / `Ag4Ms` when emitted). Target: same order of magnitude as ast-grep on warm disk for Java patterns without parse storms.
