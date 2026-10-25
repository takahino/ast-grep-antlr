# ast-grep-antlr (`ag4`)

[English](README.en.md) | 日本語

[ast-grep](https://ast-grep.github.io/) に着想を得た**非公式**の構造検索ツールです。パーサは言語ごとに使い分けます。

- **Java / C++ / Python** — [tree-sitter](https://tree-sitter.github.io/)（既定）。速度と本家互換の kind 名を優先
- **PL/SQL / SQLite / 独自 DSL** — [ANTLR4](https://www.antlr.org/)（`.g4` / `.interp`）
- **C#** — [Roslyn](https://github.com/dotnet/roslyn)（`Microsoft.CodeAnalysis.CSharp`）

公式の ast-grep プロジェクトとは**関係ありません**。

内部構造（パイプライン・パース・照合・ルールエンジン・ホスト層）は [architecture.html](architecture.html) にまとめています。

## 特長

- メタ変数パターン：`$A`、`$_`、`$$$ARGS`、型付き `${A:expression}`（supertype / ルール名）
- YAML ルール：`pattern`、`kind`、`inside`、`has`、`precedes`、`follows`、`all`/`any`/`not`、`constraints`、`matches`、`fix`、`files`/`ignores`
- `stopBy`：`neighbor`（既定、ast-grep 互換）、`end`、または kind 名
- バックエンド切り替え：`--backend tree-sitter|antlr`、`sgconfig.yml` の `languageBackends`
- 構文エラーがあっても解析を続行（tree-sitter の ERROR/MISSING、ANTLR の SLL → LL + エラー回復）
- リテラルによるプレフィルタ、gitignore、`--globs` に対応した並列ファイル探索
- 文字コードを保持（UTF-8/BOM、UTF-16/32、Shift_JIS、EUC-JP）。`--encoding` / `sgconfig.yml` で指定可能
- `.g4` / `.interp` による独自 DSL（`sgconfig.yml` → `customLanguages`、遅延読み込み）
- AI エージェント向け MCP サーバ：`ag4 mcp`（stdio）
- 組み込み言語：**Java**、**C++**、**C#**（Roslyn）、**Python** 3、**PL/SQL**、**SQLite**（独自文法で追加可能）

## インストール

```bash
# ソースから
dotnet tool install -g AstGrepAntlr.Cli --add-source ./artifacts/nupkg

# または直接実行
dotnet run --project src/AstGrepAntlr.Cli -- run -p 'System.out.println($A)' -l java src/
```

NativeAOT バイナリは GitHub Releases で配布しています（`win-x64` / `linux-x64` / `osx-arm64`）。tree-sitter のネイティブライブラリ（`tree-sitter` / `tree-sitter-java` など）はバイナリと同じ配布物に同梱され、通常は `runtimes/<rid>/native/` に置かれます。

## クイックスタート

```bash
ag4 run -p 'System.out.println($A)' -l java path/to/src
ag4 run -p 'System.out.println($A)' -l java --backend antlr path/to/src   # ANTLR に切替
ag4 run -p 'System.out.println($A)' -l java --json path/to/src
ag4 run -p 'foo($A)' -l java --strictness smart --max-results 20 --globs '**/*.java' src/
ag4 run -p 'foo($A)' -l java --rewrite 'bar($A)' --apply src/
ag4 scan -r rules/no-println.yml path/to/src
ag4 scan -c sgconfig.yml --error-on warning path/to/src
ag4 debug parse -l java --code 'class A {}'
ag4 debug pattern -l java -p 'System.out.println($A)'
ag4 version
ag4 mcp --root .                 # 読み取り専用の MCP
ag4 mcp --root . --allow-write   # rewrite_apply を有効化
```

### 終了コード（ast-grep 互換）

| コマンド | 0                                                    | 1                        | 2                                                 |
| -------- | ---------------------------------------------------- | ------------------------ | ------------------------------------------------- |
| `run`    | 1 件以上マッチ                                       | マッチなし               | 実行時エラー（パスが存在しない、不正なパターン…） |
| `scan`   | `--error-on`（既定 `error`）以上の重要度のマッチなし | しきい値以上のマッチあり | 実行時エラー（ルールが 0 件の場合も含む）         |

stderr のメッセージには `ag4:` が付きます。スタックトレースは `--debug` で表示します。

### JSON 出力

`--json` / `--json=pretty|stream|compact`（既定 `pretty`）。フィールドは可能な範囲で ast-grep に揃えています。

- `range` の行・列は 0 始まり（テキスト表示は 1 始まり）
- `byteOffset`（ファイルの文字コードでのバイト位置、BOM 込み）と `charOffset`（UTF-16）
- 範囲付きの `metaVariables.single` / `multi` / `transformed`
- `lines`、リライト時は `replacement` / `replacementOffsets`
- `file` はカレントディレクトリからの相対パス
- 判明している場合は `encoding`

### 言語 id

| Id       | 別名               | 拡張子                                                     | 既定バックエンド | 切り替え                   |
| -------- | ------------------ | ---------------------------------------------------------- | ---------------- | -------------------------- |
| `java`   |                    | `.java`                                                    | tree-sitter      | `--backend antlr`          |
| `cpp`    | `c++`, `cc`, `cxx` | `.cpp`, `.cc`, `.cxx`, `.c++`, `.hpp`, `.hh`, `.hxx`, `.h` | tree-sitter      | `--backend antlr`（CPP14） |
| `csharp` | `cs`, `c#`         | `.cs`                                                      | Roslyn           | —                          |
| `python` | `py`               | `.py`                                                      | tree-sitter      | `--backend antlr`          |
| `plsql`  | `pl/sql`           | `.pls`, `.pkb`, `.pks`, `.sql`                             | ANTLR            | —                          |
| `sqlite` |                    | `.sql`, `.sqlite`                                          | ANTLR            | —                          |

tree-sitter 既定時の kind 名は本家 ast-grep と同じ（例: `method_invocation`、`class_declaration`）。`--backend antlr` では grammars-v4 のルール名（例: `methodCall`、`compilationunit`）になります。

C++ を ANTLR にすると grammars-v4 の **CPP14**（MIT）になります。C++17 以降やマクロ多用コードは解析できないことがあります。tree-sitter 既定では tree-sitter-cpp を使います。

Python を ANTLR にすると grammars-v4 の **python3**（MIT）になります。tree-sitter 既定ではインデントは外部スキャナが処理します。

PL/SQL は grammars-v4 の **sql/plsql**（Apache-2.0）です。`.sql` は SQLite にも割り当てられているので、方言は `-l` で選んでください。

C# は **Roslyn**（MIT）です。ノードの kind は Roslyn の `SyntaxKind` を snake_case にした名前で、tree-sitter-c-sharp と名前が異なるものには別名を付けています。

## バックエンドの選択

```bash
# CLI（java / cpp / python のみ）
ag4 run -l java --backend tree-sitter -p '$F($$$ARGS)' src/
ag4 run -l java --backend antlr -p '$F($$$ARGS)' src/
```

```yaml
# sgconfig.yml
languageBackends:
  java: antlr # このリポジトリだけ ANTLR kind の既存ルールを維持
  python: tree-sitter
```

CLI の `--backend` は sgconfig より優先し、切替可能な全言語に適用されます。tree-sitter のネイティブライブラリが読めない環境では stderr に警告を出して ANTLR に自動フォールバックします。

### kind 名の移行（ANTLR → tree-sitter）

| ANTLR（例）                               | tree-sitter（例）              |
| ----------------------------------------- | ------------------------------ |
| `methodDeclaration` / `methoddeclaration` | `method_declaration`           |
| `methodCall`                              | `method_invocation`            |
| `compilationunit`                         | `program`                      |
| `expression`                              | `expression`（supertype 別名） |

`${A:expression}` は tree-sitter でも supertype 別名経由で使えます。既存 YAML の `kind:` を更新するか、一時的に `languageBackends: { java: antlr }` で旧バックエンドを維持してください。

### Cursor の MCP 設定（`.cursor/mcp.json`）

```json
{
  "mcpServers": {
    "ag4": {
      "command": "ag4",
      "args": ["mcp", "--root", "."]
    }
  }
}
```

## 独自 DSL

```yaml
# sgconfig.yml
encoding: utf-8
ruleDirs: [rules]
utilDirs: [utils]
customLanguages:
  calc:
    grammar: [grammars/CalcLexer.g4, grammars/CalcParser.g4]
    extensions: [calc]
    startRule: program
    patternRules: [statement, expr, program]
    metaVarVariants: ["{0}", "{0};"]
```

Java と `tools/antlr-4.13.2-complete.jar` が必要です（探索順：`--antlr-jar`、`ANTLR_JAR`、sgconfig のあるディレクトリ／カレントディレクトリとその祖先の `tools/`、最後に `BaseDirectory`）。生成済みの `.interp` ファイルを指定することもできます。[samples/dsl](samples/dsl) を参照してください。

`run` / `debug` / `scan` は `-c/--config` を受け付けます。独自言語は遅延登録されるので、`run -l java` で無関係な文法がコンパイルされることはありません。

### パターンのコツ

- `patternRules` は具体的なものから並べる（式・文を開始ルールより前に）。tree-sitter では文脈テンプレート名（`expression`、`statement`、`block` など）です。
- ブロックやクラス本体の中の複数メタ変数は、言語の `metaVarVariants`（`{0}`、`{0};`、`int {0};` など）で解析されます。
- 型付きメタ変数 `${A:expression}` は、照合時に `KindMatches` で検査されます。

## 埋め込み / サンプル GUI

他の言語からは、小さな C ABI を持つ NativeAOT 共有ライブラリ（`ag4_native`）を使います（`ag4_search` は JSON を受けて JSON を返し、ファイルシステムの `paths` とメモリ上の `sources` のどちらでも検索できます。任意で `backend` を指定できます）。CLI ではなく **DLL を読み込む**サンプル GUI があります。

| 言語   | UI       | FFI      |
| ------ | -------- | -------- |
| C#     | WinForms | P/Invoke |
| Java   | Swing    | JNA      |
| Python | tkinter  | ctypes   |

[samples/gui/README.md](samples/gui/README.md) を参照してください（`ag4_native` を publish してから各サンプルを実行します。tree-sitter ネイティブも同じ出力ディレクトリに配置してください）。
ヘッダ：[samples/gui/include/ag4_native.h](samples/gui/include/ag4_native.h)

.NET からプロセス内で使う場合は、`AstGrepAntlr.Core` と `AstGrepAntlr.Languages` を参照して `Searcher` を直接呼ぶこともできます。

## ast-grep との差分テスト

実在の Java / C++ / C# / Python コーパスで、バージョンを固定した `@ast-grep/cli@0.51.0` と ag4 の結果を比較します。

```bash
# 既定: ag4 は tree-sitter（java/cpp/python）
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release -- --corpus difftest/corpus.yml

# ANTLR バックエンドで比較
dotnet run --project tools/AstGrepAntlr.DiffTest -c Release -- --corpus difftest/corpus.yml --backend antlr
```

clone 先は `.difftest/`（gitignore 済み）です。結果の要約は [difftest/REPORT.md](difftest/REPORT.md)、差異の解説は [ast-grep-vs-ag4.html](ast-grep-vs-ag4.html) にあります。CI：`.github/workflows/difftest.yml`（手動実行 + 週次）。

パース時間の内訳は `tools/AstGrepAntlr.Bench`、簡易比較は [bench/compare-ast-grep.ps1](bench/compare-ast-grep.ps1)（`-Backend`、publish 済み `ag4` を優先）を参照してください。

## 開発

```bash
dotnet tool restore
npm ci
dotnet build AstGrepAntlr.slnx -c Release
dotnet test AstGrepAntlr.slnx -c Release
dotnet csharpier check .
npx prettier --check .
```

JDK（Antlr4BuildTasks 用）と .NET SDK 10（`global.json` を参照）が必要です。tree-sitter ネイティブは NuGet（TreeSitter.DotNet）のビルド済みバイナリを使うため、**C コンパイラは不要**です。既知の問題と修正状況は [issue.md](issue.md) にあります。

## ライセンス

BSD-3-Clause。[LICENSE](LICENSE) と [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照してください。
