# ag4 既知の不具合・機能不足

調査日: 2026-10-24 / 対象ブランチ: `refactor/codebase-cleanup`（行番号は同ブランチ時点）

すべて実機で再現を確認済み。再現コマンド中の `ag4` は
`dotnet run --project src/AstGrepAntlr.Cli -c Release --no-build --` に読み替え可。

## 優先度一覧

| #           | 分類   | 概要                                                     | 影響                            |
| ----------- | ------ | -------------------------------------------------------- | ------------------------------- |
| [B1](#b1)   | 不具合 | 非 UTF-8 ファイルを読み書きすると内容が破壊される        | **データ損失**                  |
| [B2](#b2)   | 不具合 | パターンが「ファイル全体ルール」で解釈され複数文で無反応 | SQLite が実質使用不能           |
| [B3](#b3)   | 不具合 | 終了コードが常に 0（違反時・例外時とも）                 | CI/lint で使用不能              |
| [B4](#b4)   | 不具合 | 例外が生スタックトレースで表示され、かつ終了コード 0     | 失敗を検知できない              |
| [B5](#b5)   | 不具合 | `$$$` が文列の位置で使えない                             | 主要ユースケース不可            |
| [B6](#b6)   | 不具合 | 単一メタ変数 `$A` が複数要素に過剰マッチ                 | 誤検出・誤置換                  |
| [B7](#b7)   | 不具合 | JSON の `byteOffset` が実バイト値でない                  | ast-grep 互換性・外部連携のズレ |
| [B8](#b8)   | 不具合 | `CollapseChains` がチェーンを分断する                    | `kind` 一致漏れ                 |
| [B9](#b9)   | 不具合 | `$$$NAME` のテンプレート置換が到達不能                   | 置換結果が壊れる                |
| [B10](#b10) | 不具合 | 存在しないパスを黙殺する                                 | タイポに気づけない              |
| [B11](#b11) | 不具合 | `scan` で `-r`/`-c` 未指定時に無言で成功                 | 設定ミスに気づけない            |
| [B12](#b12) | 不具合 | `id` 未指定ルールが複数あると fix 対象が混線             | 誤置換                          |
| [B13](#b13) | 不具合 | 差分出力が全行 `-`/`+` の擬似 unified diff               | プレビューが実用に耐えない      |
| [M1](#m1)   | 機能   | 文字コードの自動判定と `--encoding` 明示指定             | 日本語環境で必須                |
| [M2](#m2)   | 機能   | `run`/`debug` に `--config` がなくカスタム言語を使えない | DSL のデバッグ不可              |
| [M3](#m3)   | 機能   | `precedes` / `follows` 未実装                            | ルール表現力不足                |
| [M4](#m4)   | 機能   | `${A:expression}` の型制約が効かない                     | README との乖離                 |
| [M5](#m5)   | 機能   | `files` / `utilDirs` が未使用                            | 設定が無視される                |
| [M6](#m6)   | 機能   | `--strictness` が CLI にない（MCP にはある）             | 機能面の不整合                  |
| [M7](#m7)   | 機能   | ANTLR jar 探索がカレントディレクトリ直下のみ             | 実行場所に依存                  |
| [M8](#m8)   | 機能   | `.gitignore` 対応が簡易すぎる                            | 除外漏れ・過剰除外              |
| [M9](#m9)   | 機能   | `version` がハードコード                                 | 配布物の識別不可                |
| [M10](#m10) | 機能   | テストが 5 件のみ                                        | 回帰を検知できない              |

---

## 不具合

<a id="b1"></a>

### B1. 非 UTF-8 ファイルを読み書きすると内容が破壊される

**影響: データ損失。最優先。**

読み込みは `File.ReadAllText` 固定（`src/AstGrepAntlr.Core/Search/Searcher.cs:132`）。.NET は BOM があれば
UTF-8/UTF-16/UTF-32 を判定するが、**BOM なしの Shift_JIS / EUC-JP は UTF-8 として読まれ、不正バイトが
U+FFFD に置換される**。この時点で検索結果のテキストもオフセットも信用できない。

さらに致命的なのが書き戻しで、`Rewriter.DetectEncoding`（`src/AstGrepAntlr.Core/Rewrite/Rewriter.cs:92-100`）は
**UTF-8 BOM の有無しか見ていない**。UTF-16 の BOM すら判定対象外のため、

- Shift_JIS ファイル → U+FFFD 混じりの UTF-8 で上書き（原文復元不可）
- UTF-16 ファイル → UTF-8 で上書き

という破壊が `--apply` / `--update-all` / MCP の `rewrite_apply` で発生する。
`ApplyAtomic` のハッシュ検証は「読み込み後の文字列」同士の比較なので、この破壊を検知できない。

**提案**

1. バイト列を正として扱う。読み込みは `File.ReadAllBytes` → エンコーディング決定 → デコード、
   書き戻しは**読み込み時に確定した同一エンコーディング**で行う（BOM の有無も保存・復元する）。
2. 判定順序: 明示指定 > BOM > 推定（UTF-8 として妥当か検証し、不正なら既定のレガシーコードページ）。
3. デコードに `DecoderFallback.ExceptionFallback` を使い、**判定失敗を黙って U+FFFD にしない**。
   判定できないファイルはスキップし、`--show-errors` で理由を出す。
4. 判定結果を `RewriteChange` に持たせ、`ApplyAtomic` がそれを使って書き戻す。
5. ハッシュ検証は文字列ではなく**生バイト**に対して行う。
6. 併せて [M1](#m1) の `--encoding` を実装する。

Shift_JIS 等を扱うには `System.Text.Encoding.CodePages` パッケージと
`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` が必要。

<a id="b2"></a>

### B2. パターンが「ファイル全体ルール」で解釈され、複数文のファイルでマッチしない

```bash
printf 'SELECT a, b FROM t WHERE x = 1;\nselect c from u;\n' > /tmp/q.sql
ag4 run -p 'SELECT $$$C FROM $T' -l sqlite /tmp/q.sql   # → 0 件

printf 'SELECT a, b FROM t;\n' > /tmp/one.sql
ag4 run -p 'SELECT $$$C FROM $T' -l sqlite /tmp/one.sql # → 1 件（文が 1 つだけなら一致）
```

`ag4 debug pattern -l sqlite -p 'SELECT $$$C FROM $T'` が `entry: parse` と表示する通り、パターンが
最上位ルール `parse`（＝ファイル全体の文リスト）としてコンパイルされている。結果、パターンが
「ファイル全体の構造」と一致することを要求し、文が 2 つ以上あると一致しなくなる。

原因は `PatternCompiler.Compile`（`src/AstGrepAntlr.Core/Pattern/PatternCompiler.cs:44-84`）が
`PatternRules` を先頭から試し**最初に成功したものを採用する**ことと、
`SqliteLanguage.PatternRules`（`src/AstGrepAntlr.Languages/Sqlite/SqliteLanguage.cs:12-13`）の先頭が
`parse` であること。Java は `compilationunit` では式をパースできず自然に後続候補へ落ちるため露見していない
（`JavaLanguage.cs:12-23` も先頭は `compilationunit` で、同じ地雷を踏んでいる）。

`samples/dsl/sgconfig.yml` でも同じ理由でサンプルが 0 件になっていた（当該ファイルは順序変更で暫定対処済み）。

**提案**（どちらか、できれば両方）

- 恒久策: パターン木のルートを降下させる。`EOF` を除いた有意な子が 1 つだけなら、その子をルートとして
  採用する。全体ルールでパースされても「1 文のパターン」に正規化できる。
- 暫定策: 組み込み言語の `PatternRules` を具体的なものから並べ替える。ただしユーザー定義 DSL では
  同じ罠が再発するため、恒久策とセットで。

<a id="b3"></a>

### B3. 終了コードが常に 0

```bash
ag4 scan -r rule.yml path/ ; echo $?          # 違反検出 → 0
ag4 run -p 'x' -l nosuchlang path/ ; echo $?  # 例外終了 → 0
ag4 run -p 'bar($A)' -l java /no/such ; echo $?  # 存在しないパス → 0
```

違反の有無も失敗も終了コードに反映されないため、CI の lint ステップとして機能しない。

**提案**: 0 = 一致なし / 1 = 一致あり / 2 = 実行エラー を基本とする（grep 慣例）。
`scan` は `severity` を見て `error` 検出時のみ 1 を返す等の制御も欲しい（[M9](#m9) 相当）。
破壊的変更になるため、`--error-on-match` 等での段階導入も検討の価値あり。

<a id="b4"></a>

### B4. 例外が生スタックトレースで表示され、終了コードも 0

```bash
ag4 run -p 'x' -l nosuchlang /tmp          # KeyNotFoundException のスタックトレース
ag4 debug pattern -l java -p 'if ($C) { $$$B }'  # InvalidOperationException のスタックトレース
```

`Program.cs` の各 `SetAction` に例外処理がなく、`System.CommandLine` がそのまま出力する。
`LanguageRegistry.Get` は「Known: ...」という親切なメッセージを持っているのに、スタックトレースに
埋もれて読めない。

**提案**: トップレベルで例外を捕捉し、`ag4: <message>` を stderr に出して終了コード 2 を返す。
`--debug` 指定時のみスタックトレースを出す。

<a id="b5"></a>

### B5. `$$$` が文列の位置で使えない

```bash
ag4 debug pattern -l java -p 'void $M() { $$$BODY }'    # Failed to parse pattern as any of [...]
ag4 debug pattern -l java -p 'if ($C) { $$$B }'         # 同上
ag4 debug pattern -l java -p 'class $N { $$$BODY }'     # 同上
ag4 debug pattern -l java -p 'foo($$$A)'                # こちらは成功
```

メタ変数は `MetaVarPlaceholderTemplate`（`__MV{0}__`）で**識別子 1 個**に置換される
（`PatternCompiler.cs:38-42`、確認: `foo($$$A)` のパターン木に `IDENTIFIER '__MV0__'` が出る）。
式が来る位置なら識別子として成立するが、**文・宣言が来る位置では構文的に不正**になりパースできない。

「メソッド本体の任意の文列」「クラス本体の任意のメンバ列」という ast-grep の主要ユースケースが
丸ごと使えない。

**提案**: 文脈に応じたプレースホルダが必要。たとえば文の位置では `__MV0__;`（式文）として埋め、
パース後にそのノードをメタ変数として標識する、といった方式。言語ごとに
「文位置のプレースホルダ雛形」を `ILanguage` に持たせるのが素直。

<a id="b6"></a>

### B6. 単一メタ変数 `$A` が複数要素に過剰マッチ

```bash
printf 'class T { void a() { foo(1, 2, 3); } }\n' > /tmp/T.java
ag4 run -p 'foo($A)' -l java --json /tmp/T.java
# → foo(1, 2, 3) に一致し、metaVariables.A.text == "1, 2, 3"
```

ast-grep では `$A` は単一ノードにのみ一致し、`foo(1,2,3)` は `foo($$$A)` でしか捕まらない。
`PatternMatcher.MatchNode`（`src/AstGrepAntlr.Core/Matching/PatternMatcher.cs`）のメタ変数処理が
対象ノードのテキストを無条件に捕捉するため、引数リスト全体に一致してしまう。

誤検出に留まらず、`--rewrite` と組み合わせると意図しない置換を生む。

**提案**: 単一メタ変数は「対象ノードが 1 つの子要素に対応する場合のみ」一致させる。
区切りトークン（`,` など）を含むノードに一致させない制約が要る。

<a id="b7"></a>

### B7. JSON の `byteOffset` が実バイト値でない

```bash
# 日本語リテラルを含む Java ファイルに対して
ag4 run -p 'bar($A)' -l java --json U.java
# → byteOffset.start = 49
grep -ob 'bar(9)' U.java
# → 55（実バイト位置）
```

差分 6 は日本語 3 文字分（3 バイト − 1 UTF-16 単位）× 3。つまり報告値は
**UTF-16 コードユニット単位のオフセット**であり、`byteOffset` という名前と実体が食い違う
（`src/AstGrepAntlr.Core/Output/Printers.cs:52`）。ast-grep の JSON は実バイトを返すため、
出力を取り込む外部ツールで位置がずれる。

**提案**: 名称どおり UTF-8 バイトオフセットを出す（[B1](#b1) のバイト列ベース化と合わせて実装するのが自然）。
互換性を壊したくない場合は `charOffset` を併記し、`byteOffset` を正しい値に直す。

<a id="b8"></a>

### B8. `CollapseChains` が単一子チェーンを分断する

`src/AstGrepAntlr.Core/Tree/TreeBuilder.cs:144-177`

チェーン `5 → 6 → 7` を `i=5` で処理して `ChainHead` を 5 に設定した後、ループが `i=6` に進むと
6 も「チェーン先頭」として再処理され、`nodes[6] = nodes[6] with { ChainHead = 6 }`（173 行目）で
**外側チェーンの情報を上書き**してしまう。結果、ノード 7 からは内側チェーンの種別しか見えず、
`KindMatches` が外側のルール名に一致しなくなる。

**提案**: 既に別のチェーンへ取り込まれたノード（`ChainHead != i`）は先頭として処理しない。
`BuildNode` が `ChainHead = index` で初期化し、親が先に処理される（pre-order）ため、
ループ先頭で `if (nodes[i].Kind != Rule || nodes[i].ChainHead != i) continue;` とすれば足りる。
173 行目の再代入も不要になる。

**注意**: `kind` の一致範囲が広がる＝挙動が変わる。修正と同時に回帰テストが必要。

<a id="b9"></a>

### B9. `$$$NAME` のテンプレート置換が到達不能

`src/AstGrepAntlr.Core/Rewrite/Rewriter.cs:11-22`

```csharp
result = result.Replace("$" + kv.Key, kv.Value, ...);      // 先に $NAME を置換
result = result.Replace("$$$" + kv.Key, kv.Value, ...);    // ここには決して到達しない
```

`$$$ARGS` は 1 行目で `$`+`$$ARGS` として部分置換され、2 行目の条件を満たさなくなる。
コメントには「Longer names first」とあるが、ソートしているのは**メタ変数名の長さ**であって
プレフィックスの長さではないため、意図が達成されていない。

**提案**: 長いプレフィックスから処理する（`$$$NAME` → `$NAME` → `$$$`）。
正規表現で 1 パス置換すれば取りこぼしと二重置換の両方を防げる。

<a id="b10"></a>

### B10. 存在しないパスを黙殺する

```bash
ag4 run -p 'bar($A)' -l java /no/such/path   # 出力なし・終了コード 0
```

`FileWalker.Enumerate` は `File.Exists` でも `Directory.Exists` でもないルートを
`continue` で読み飛ばす（`src/AstGrepAntlr.Core/Search/FileWalker.cs:33-36`）。
パスのタイポが「一致 0 件」と区別できない。

**提案**: 存在しないルートは stderr に警告を出し、終了コードを 2 にする。

<a id="b11"></a>

### B11. `scan` で `-r` も `-c` も指定しないと無言で成功する

```bash
ag4 scan /tmp/path    # 出力なし・終了コード 0
```

ルールが 0 件でも検査が走った扱いになる。CI で設定ミスがあっても緑になる。

**提案**: ルールが 1 件も読み込めなかった場合はエラーとして扱う。
ast-grep 同様、カレントから `sgconfig.yml` を自動探索する挙動も検討したい。

<a id="b12"></a>

### B12. `id` 未指定ルールが複数あると fix 対象が混線する

`src/AstGrepAntlr.Cli/Program.cs:140`

```csharp
var byFile = all.Where(m => m.RuleId == doc.Id).GroupBy(m => m.File);
```

`doc.Id` が null のとき `m.RuleId == null` の一致**すべて**が対象になる。`id` を書いていない
ルールが複数あり、かつ `fix` を持つ場合、別ルールの一致箇所に対して誤った fix が適用される。

また `all` は全ルールの結果を累積するため、ルール数に比例して走査コストが増える（N+1 的構造）。

**提案**: ルールごとの一致結果をローカル変数で受けて `all` と分離する。
併せて `id` 未指定ルールを警告対象にする。

<a id="b13"></a>

### B13. 差分出力が全行 `-`/`+` の擬似 unified diff

`src/AstGrepAntlr.Core/Rewrite/Rewriter.cs:102-127`（`// Simple full-file diff for MVP (not LCS)`）

1 箇所の置換でもファイル全行が削除・追加として出力される。実ファイルでは差分が読めず、
`rewrite_preview` を使う MCP クライアント側でも同様。

**提案**: 行単位 LCS（Myers 等）で実差分を出し、前後数行のコンテキスト付きハンクにする。

---

## 機能不足

<a id="m1"></a>

### M1. 文字コードの自動判定と明示指定

[B1](#b1) の対になる要望。

- `--encoding <name>`（例 `utf-8`, `shift_jis`, `euc-jp`, `utf-16`）で明示指定できること
- 未指定時は BOM → 内容推定の順に自動判定すること
- 判定結果を `--json` 出力に含めると、取り込み側が扱いやすい
- 書き戻しは読み込み時のエンコーディングと BOM 有無を保持すること
- `sgconfig.yml` の言語定義や `encoding:` キーでの既定値指定も欲しい

実装には `System.Text.Encoding.CodePages` の参照追加と
`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` の初期化が必要。

<a id="m2"></a>

### M2. `run` / `debug` に `--config` がない

`CustomLanguageBootstrap.ApplyConfig` は `scan`（`Program.cs:115`）と `mcp`（`Program.cs:221`）でしか
呼ばれない。そのため `sgconfig.yml` で定義したカスタム言語は

- `ag4 run -p ... -l calc` で使えない
- `ag4 debug parse/pattern -l calc` でデバッグできない

[B2](#b2) のようなパターン解釈の問題を DSL 利用者が自力で切り分ける手段がない。

**提案**: `--config` を共通オプションとして `run` と `debug` にも追加する。

<a id="m3"></a>

### M3. `precedes` / `follows` 未実装

`RuleLoader` は両キーを読み `RuleNode` に格納する（`RuleLoader.cs:160-168`、`RuleModels.cs:28-29`）が、
`RuleEngine.MatchRule` は一切参照しない。**YAML に書いても黙って無視される**ため、
誤ったルールが「一致 0 件」として通ってしまう。

**提案**: 実装する。当面実装しないなら、ロード時に「未対応」と警告する。

<a id="m4"></a>

### M4. `${A:expression}` の型制約が効かない

README に拡張構文として記載があり、`MetaVar` の正規表現も `TypedRule` を取り出すが、
その値を使う経路が存在しない（型制約を担うはずだった `TypedMetaVar` は全体が未使用のため
リファクタで削除済み）。現状 `${A:expression}` は `$A` と完全に同義で、ルール名は無視される。

```bash
ag4 run -p '${A:expression} + 1' -l java /tmp   # 構文は通るが expression 制約は効いていない
```

**提案**: 捕捉テキストを当該ルールで再パースして検証する（`constraints.kind` と同等の仕組みで実現可能）。
実装しないなら README から記載を削る。

<a id="m5"></a>

### M5. `files` / `utilDirs` が未使用

- `SgRuleDocument.Files`（`RuleModels.cs:12`）: ロードされるが `Searcher` は参照しない。
  ルール側でのファイル絞り込みが効かない（`ignores` は有効）。
- `SgConfig.UtilDirs`（`RuleModels.cs:49`）: ロードされるが利用経路がない。
  共有ユーティリティルールをディレクトリから読む機能が未完成。

**提案**: 実装するか、未対応である旨を警告する。

<a id="m6"></a>

### M6. `--strictness` が CLI にない

MCP の `find_code` には `strictness` パラメータがある（`McpHost.cs`）一方、`ag4 run` には対応する
オプションがなく、常に `Strictness.Smart` 固定（`Searcher.RunPattern` の既定引数）。
`cst` / `ast` / `relaxed` を CLI から選べない。

**提案**: `run` に `--strictness` を追加する。併せて `--max-results`、`--context`（前後行表示）、
`--glob` といった一般的なオプションの有無も整理したい（`FileWalker` のグロブ対応は現状なし）。

<a id="m7"></a>

### M7. ANTLR jar 探索がカレントディレクトリ直下のみ

`GrammarInterpCache.FindAntlrJar`（`src/AstGrepAntlr.Core/Language/InterpretedLanguage.cs:347-353`）が
見るのは `tools/antlr-4.13.2-complete.jar`（CWD 相対）と `AppContext.BaseDirectory/tools/...` の 2 つだけ。

```bash
cd samples/dsl && ag4 scan -c sgconfig.yml .
# → ANTLR tool jar not found.（リポジトリルートから実行すれば成功する）
```

`sgconfig.yml` のあるディレクトリで作業する自然な使い方で失敗する。

**提案**: `sgconfig.yml` のあるディレクトリから祖先を遡って `tools/` を探す。
`--antlr-jar` オプションや `ANTLR_JAR` 環境変数での明示指定も欲しい。

<a id="m8"></a>

### M8. `.gitignore` 対応が簡易すぎる

`FileWalker.LoadGitignores` / `IsGitIgnored`（`src/AstGrepAntlr.Core/Search/FileWalker.cs:96-140`）の制限:

- 読むのは**探索ルート直下の `.gitignore` のみ**。サブディレクトリやグローバル設定、
  親ディレクトリの `.gitignore` は無視
- ネガーションパターン（`!foo`）非対応。除外すべきでないファイルが除外される
- `*` / `**` / 文字クラスなどのグロブ非対応。`EndsWith` / `Contains` による近似マッチのため
  **意図しないファイルまで除外される**可能性がある
- `bin` / `obj` / `node_modules` / `.git` は常にスキップされ、無効化手段がない
  （`SkippedDirectories` 定数）。`bin` という名のソースディレクトリは検索対象にできない

**提案**: 本格対応するならライブラリ導入か、`git ls-files` への委譲を検討。
少なくともネガーションとグロブは対応したい。

<a id="m9"></a>

### M9. `severity` に応じた終了コード制御

[B3](#b3) の一部。ルールの `severity`（`error` / `warning` / `info`）がロードされるのに、
出力の装飾にも終了コードにも反映されていない。`error` のみで CI を落とす制御が欲しい。

**提案**: `--error-on <severity>` 相当のオプションを用意する。

<a id="m10"></a>

### M10. テストが 5 件のみ

`tests/AstGrepAntlr.Tests` は Java パース 3 件、ルール YAML 1 件、Searcher 1 件のみ。
本ドキュメントの不具合はいずれも既存テストをすり抜けている。とくに未カバーなのは:

- カスタム言語（`.interp` 読み込み・`.g4` コンパイル）— つい先日まで**全く動作していなかった**が検知できず
- SQLite 言語
- 書き換え（`ApplyTemplate` / `PreviewFile` / `ApplyAtomic`）
- 文字コードの異なる入力
- メタ変数の各形態（`$_` / `$$$` / 型付き）
- CLI の終了コードと引数処理
- MCP ツール群

**提案**: 上記を優先してテストを追加する。とくに [B1](#b1) 修正時は
非 UTF-8 ファイルの読み書き往復テストを必須にしたい。

---

## 補足: 調査時に確認した正常動作

退行判定の基準として記録する。

- `$$$ARGS` による可変長引数の一致（`foo($$$ARGS)` が `foo(1,2,3)` と `foo()` の両方に一致）
- `$_` ワイルドカード
- `constraints` の `regex` / `kind`（一致・不一致とも期待どおり）
- `inside` による絞り込み
- `--rewrite` のプレビューと `--apply` の書き換え
- 複数ルールでの `--update-all`（ルールごとに再検索するためオフセットは破綻しない）
- 構文エラーを含む Java ファイルの継続パース
- MCP サーバの起動と `initialize` 応答
- カスタム DSL のスキャン（`samples/dsl`、`patternRules` 順序の修正後）
