# C++14 grammar (grammars-v4)

Vendored from [antlr/grammars-v4](https://github.com/antlr/grammars-v4) `cpp/` directory.

- **Commit:** `5a0ddb4aa12c1502d7bd5ec8fb8673e607b3b191`
- **License:** MIT — see [LICENSE](./LICENSE) and file headers in `CPP14Lexer.g4` / `CPP14Parser.g4`

Files:

- `CPP14Lexer.g4`, `CPP14Parser.g4` — ANTLR grammars
- `CSharp/CPP14ParserBase.cs` — C# parser base class (namespace adjusted for this repo)

## ag4 独自の修正（取り込み直すときは再適用すること）

`CPP14Parser.g4` から、**曖昧なときに必ず負けていた選択肢**を外した。ANTLR は曖昧な入力を番号の小さい
選択肢に解決するので、残す選択肢と同じ入力にしか一致しない後ろの選択肢は選ばれない。選ばれないのに、
型名が出るたびに完全文脈の曖昧さ判定（LL フォールバック）を起こしていた。

| ルール                | 外した選択肢                                     | 理由                                               |
| --------------------- | ------------------------------------------------ | -------------------------------------------------- |
| `theTypeName`         | `enumName` / `typedefName` / `simpleTemplateId` | いずれも `className` と同じ入力にしか一致しない    |
| `nestedNameSpecifier` | 先頭の `namespaceName`                           | `Identifier ::` は先にある `theTypeName` が取る    |
| `namespaceName`       | `namespaceAlias`                                 | `originalNamespaceName` と同じく `Identifier` だけ |
| `templateArgument`    | `idExpression`                                   | 同じ範囲を `constantExpression` が必ず読める       |

`.difftest/corpus` の C++ 377 ファイルで、木のハッシュ（`ag4-bench --tree-hashes`）は修正前と全件一致。

試して見送ったもの（木が変わったため）:

- `memInitializerId` の `| Identifier`: 理屈の上では `classOrDeclType` が取るはずだが、16 ファイルで木が変わった。
- `typeSpecifier` の並べ替え（`classSpecifier` を先頭に）: 3 ファイルで木が変わり、SLL の失敗も 243 → 237 にしか減らなかった。
