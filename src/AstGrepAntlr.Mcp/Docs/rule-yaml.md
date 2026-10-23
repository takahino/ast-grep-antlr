# Rule YAML (ast-grep compatible subset)

```yaml
id: example
language: java
severity: warning
message: ...
rule:
  pattern: System.out.println($A)
  inside:
    kind: methodDeclaration
    stopBy: end
constraints:
  A:
    kind: literal
fix: logger.info($A)
```

Composite: `all`, `any`, `not`, `matches`. Relational: `inside`, `has`, `precedes`, `follows`.
