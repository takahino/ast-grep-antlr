# Rule YAML (ast-grep compatible subset)

```yaml
id: example
language: java
severity: warning
message: ...
rule:
  pattern: System.out.println($A)
  inside:
    kind: method_declaration
    stopBy: end
constraints:
  A:
    kind: string_literal
fix: logger.info($A)
```

Composite: `all`, `any`, `not`, `matches`. Relational: `inside`, `has`, `precedes`, `follows`.

Kind names depend on the language backend:

- **tree-sitter** (default for Java / C++ / Python / C#; only option for JavaScript / TypeScript / Go / Rust / C / Ruby / PHP and the other tree-sitter-only languages): upstream-style names such as `method_declaration`, `call_expression`, `invocation_expression`
- **ANTLR** (`--backend antlr` / `languageBackends`): grammar rule names such as `methodDeclaration` (Java / C++ / Python)
- **Roslyn** (`--backend roslyn` / `languageBackends: { csharp: roslyn }`): Roslyn `SyntaxKind` snake_case with some tree-sitter-c-sharp aliases
