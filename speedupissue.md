# 高速化の調査レポート

ボトルネックはマッチングではなく、**パース処理（tree-sitter / ANTLR）** と **CLI のプロセス起動** にある。

## 前提：ag4 の独自性

高速化の方針は、ag4 の次の独自性を損なわないことを条件に決める。

1. **.NET から簡単に呼べる**（NuGet で配るライブラリ）
2. **別言語から呼べる DLL**（`ag4_native`：NativeAOT で C ABI を公開）
3. **ANTLR の文法（BNF 記法）で、tree-sitter にない言語にも対応できる**（`.g4` を実行時に読む `InterpretedLanguage` を含む）

## 方針：Rust は導入せず、C# のまま改善する

パースやマッチングを Rust に移す案も検討したが、採用しない。

| 独自性                                  | Rust を入れた場合の影響                                                                                                                                              |
| --------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| ① .NET から簡単に呼べる                 | 悪化する。RID ごとの Rust のネイティブ DLL が配布物に増え、ビルドと CI に Rust のツールチェーンが必要になる                                                          |
| ② 別言語から呼べる DLL                  | 改善しない。`ag4_native` はすでに NativeAOT で C ABI を公開しており、起動もネイティブ並みに速い                                                                      |
| ③ ANTLR で tree-sitter にない言語に対応 | 改善しない。Rust 版の ANTLR ランタイム（antlr-rust）は 4.8 止まりでメンテナンスもほぼ止まっている。`InterpretedLanguage` も C# の `ParserInterpreter` に依存している |

そのほかの理由:

- tree-sitter のパーサー本体はもともと C のネイティブコード。遅いのは C# へツリーを移す部分（P/Invoke の回数）なので、C# 側の工夫（下記 2）で改善できる。
- マッチングは全体の 2% 以下。Rust にしても速度上の効果はほぼない。
- ast-grep のマッチャー（`ast-grep-core`）に置き換えると、ANTLR や Roslyn のツリーを扱えない。tree-sitter の言語と ANTLR の言語でマッチングの挙動が分かれてしまい、独自性の ③ とぶつかる。

## 計測結果

Release ビルド、`.difftest/corpus` を使用。`tools/AstGrepAntlr.Bench`（シングルスレッド）で計測した。

| 対象                                                         | read  | parse            | match |
| ------------------------------------------------------------ | ----- | ---------------- | ----- |
| Java / tree-sitter（commons-lang 264 ファイル、37 万ノード） | 17ms  | **1096ms (96%)** | 23ms  |
| Java / ANTLR（同じファイル）                                 | 31ms  | **7014ms (98%)** | 132ms |
| C# / tree-sitter（corpus 全体 1587 ファイル）                | 626ms | **9164ms (93%)** | 33ms  |

CLI（`ag4.exe`、並列実行あり）で一連の処理を通した場合:

- `samples` へのマッチしないパターンでも約 **0.33s** かかる。大半は起動時の固定コストとみられる（内訳は未計測）
- commons-lang 全体を `return $A;` で検索すると 0.64s。うち約半分が起動コスト

## 改善案（優先順）

### 1. ANTLR の経路 — 最優先（独自性 ③ の本体）

tree-sitter より約 6 倍遅く、改善する価値が最も大きい。

- **LL へのやり直し率を計測する** — `ParseResult.UsedLlFallback` はすでにあるので、Bench に表示を足す。文法によって SLL で通る割合が大きく変わるので、速度への影響はおそらくこれが最大。ユーザーが自作の `.g4` を持ち込む前提なら、「この文法は LL に落ちやすい」と診断できる機能は、それ自体が利用者にとっての価値になる。
- **パース本体とツリー変換の内訳を測る** — 7s のうち、ANTLR のパース本体と `TreeBuilder`（ANTLR の木から arena への変換）がそれぞれどれだけかを Bench で分けて測る。変換の方が重ければ、C# 側だけで改善できる。
- **SLL で作ったツリーの無駄をなくす** — `AntlrTwoStageParser` は SLL が失敗すると、組み立て済みのツリーを捨てて LL でやり直している。成功が確定してから `TreeBuilder` を呼ぶようにする。
- **`ParseTimeout` を実際に効かせる（正しさの問題）** — `cts` に `ParseTimeout` を設定しているが、パーサーにはどこにも渡していない。
- **DFA キャッシュを再利用できる使い方を推す** — ANTLR の予測用 DFA は、プロセスの中で温まっていく（`InterpretedLanguage` も `SharedDfaParserInterpreter` で共有済み）。一方、CLI を 1 回ずつ起動すると、毎回冷えた状態から始まる。DLL への組み込み・MCP・`--watch` のように同じプロセスを使い続ける形を、ドキュメントで推奨する。

### 2. tree-sitter の経路 — C# のまま、境界を越える回数を減らす

37 万ノードで 1.1s（1 ノードあたり約 3µs）は、tree-sitter ネイティブのパース速度としては遅い。ツリーを移すときのネイティブ呼び出しのコストが主因と推測している（ネイティブのパースとツリー変換の内訳は未計測）。

- **子ノードへの添字アクセスが O(n²) になる** — `TreeSitterTreeBuilder.cs:89`（`BuildNode`）と `:185`（`BuildNodeShifted`）は `ts_node_child(node, i)` で子をたどっている。この関数は先頭から辿るので 1 回が O(i) かかり、子が多いノードでは全体が O(n²) になる。`TSTreeCursor`（`goto_first_child` / `goto_next_sibling`）に置き換えれば 1 ノード O(1)。cursor は `TreeSitterLanguage.cs:408` ですでに使っている。
- **P/Invoke のコスト** — 1 ノードごとに `ts_node_*` を 8〜10 回呼んでいる。しかも `bool` を `MarshalAs(I1)` で返す `DllImport` なので、毎回マーシャリング用のスタブを通る。`byte` で返す `LibraryImport` に変え、`[SuppressGCTransition]` も付ければ、呼び出し 1 回あたりのコストを下げられる。
- **エラー収集でツリーを 2 回たどっている** — `TreeSitterLanguage.Parse` の `CollectDiagnostics` が、ツリー全体をもう一度 P/Invoke でたどっている（`WalkErrors` も子を `ts_node_child` で取っている。`:232`）。先に `ts_node_has_error(root)` を確認してエラーがなければ辿らないようにすれば、大半のファイルで走査 1 回分が消える。
- **上限を見積もる** — ネイティブの `ts_parser_parse` だけにかかる時間を測っておけば、ここでどこまで縮められるかの上限がわかる。

### 3. CLI の起動時間（CLI のみ）

- `PublishReadyToRun=true`（コンパイル済みコードを同梱して JIT を減らす設定）で publish する。設定 1 行で試せる。
- 余力があれば `PublishAot` に対応する。現在は `false` で、`Directory.Build.props` に「JSON source-gen 待ち」とコメントがある。MCP や System.CommandLine 周りの対応が必要になる。
- 巨大な `PlSqlParser`（生成コード約 27 万行）などが、使わないときにも読み込まれていないか確認する。
- `ag4_native` はすでに NativeAOT なので対象外。.NET からライブラリとして使う場合も、呼び出し側のプロセスで JIT が温まるので影響は小さい。

### 4. 組み込み用 API（独自性 ② を伸ばす）

- `ag4_search` は呼び出しごとに JSON を受け取る。GUI などから何度も呼ばれる用途では、コンパイル済みパターンやパース済みツリーをホスト側にキャッシュすると効く可能性がある（`SearchHost` の中身は未確認。効果は未検証）。

### 5. マッチングと結果の生成 — 効果：小（今は全体の 2% 以下）

ルールが複雑になると効いてくる。

- `PatternMatcher.MatchSequence`：`$$$` の終わり位置を 1 つ試すたびに `Dictionary` を丸ごとコピーしている（`PatternMatcher.cs:196`）。
- `MatchChildren`：呼ばれるたびに LINQ と `ToList()` で子の一覧を作り直している。
- `SyntaxNode` は約 70 バイトの struct で、`var n = _nodes[i]` と書くたびにコピーされる。`ref readonly` で受ければコピーを避けられる。
- `SourceFile.Read` は全ファイルの SHA256 を計算しているが、使うのは出力時だけ。計算を遅らせれば、プレフィルタで弾いたファイルの分が浮く。
- `Searcher` は結果を `ConcurrentBag` に集めた後に全件ソートしている。そのため `--max-results` を指定しても、探索を早めに打ち切らない。

## 進め方

1. **計測の追加**（挙動は変わらない）— Bench に次を追加する:
   - ANTLR：LL へのやり直し率、パース本体とツリー変換の内訳
   - tree-sitter：ネイティブのパース時間とツリー変換の内訳
2. **ANTLR の改善** — SLL のツリーの無駄をなくす。`ParseTimeout` を修正する。
3. **tree-sitter の案A** — cursor 化、`ts_node_has_error` による事前判定、`LibraryImport` 化。
4. **CLI の起動** — ReadyToRun で計測し、効果があれば採用する。
5. 必要に応じて 4〜5 の項目。

各ステップの後に Bench で数値を比べ、difftest で出力が変わっていないこと（回帰がないこと）を確認する。

## 計測の再現手順

```sh
dotnet run -c Release --project tools/AstGrepAntlr.Bench -- -l java -p 'foo($$$A)' --max-files 2000 .difftest/corpus/commons-lang/src/main/java
dotnet run -c Release --project tools/AstGrepAntlr.Bench -- -l java --backend antlr -p 'foo($$$A)' --max-files 2000 .difftest/corpus/commons-lang/src/main/java
dotnet run -c Release --project tools/AstGrepAntlr.Bench -- -l csharp -p 'foo($$$A)' --max-files 2000 .difftest/corpus
```

## 対応結果（実装済み）

進め方 1〜5 を実装した。difftest の集計（same / only-ast-grep / only-ag4 / range-diff / metavar-diff）は変更前後で一致。

### 前提の補正（実装中に判明）

- Bench は以前 `ElapsedMilliseconds` をファイルごとに足していたため、1ms 未満が切り捨てられていた。ticks 積算に変更済み。
- リリース用 `ag4` は既に NativeAOT（`PublishAot=true`）。ReadyToRun が効くのは JIT 経路（`dotnet ag4.dll` / `dotnet tool`）のみ。
- SLL 失敗時は Bail が例外を投げるため、失敗パスではもともと `TreeBuilder` は呼ばれていなかった。成功確定後に 1 回だけ Build する構造に整理した。
- CLI に `--watch` はない。DFA を温める用途では DLL / NuGet（`Searcher`）/ MCP を推奨（README に追記）。

### Bench（Release、改善後）

| 対象                            | parse_ms   | 内訳（代表）                                                | 備考                                                                          |
| ------------------------------- | ---------- | ----------------------------------------------------------- | ----------------------------------------------------------------------------- |
| Java / tree-sitter（264 files） | ~590–770   | native ~300–410 / build ~260–330 / **diag ~0.2**            | 改善前 diag は ~145ms。`has_error` 事前判定でほぼ消滅                         |
| Java / ANTLR（同）              | ~4200–5000 | lex ~500 / SLL ~80–100 / **LL ~2800–3500** / build ~530–650 | **LL フォールバック 177/264（67%）** が支配的                                 |
| C# / tree-sitter（1587 files）  | ~4100      | native ~2400 / build ~1500 / **diag ~27**                   | 改善前は parse_ms≈6335、diag≈999。cursor + LibraryImport + has_error が効いた |

マッチングは全体の数％程度のまま（ticks 積算でも同様）。

### 起動時間（10 回平均、win-x64）

| 経路                      | `version` | マッチしない `run` |
| ------------------------- | --------- | ------------------ |
| NativeAOT exe             | 42ms      | 90ms               |
| ReadyToRun（FDD publish） | 169ms     | 150ms              |
| JIT（`dotnet ag4.dll`）   | 254ms     | 307ms              |

ReadyToRun は JIT より速いが AOT には劣る。リリース binary は既に AOT のため、R2R を既定にはしない。言語の遅延登録（`RegisterLazy`）は JIT / R2R の起動経路向け。

### difftest 回帰（集計一致）

| backend         | same   | only-sg | only-ag4 | range | meta  | ag4 合計（参考） |
| --------------- | ------ | ------- | -------- | ----- | ----- | ---------------- |
| tree-sitter     | 179924 | 11657   | 4528     | 0     | 9299  | 339s → 320s      |
| antlr           | 152377 | 31021   | 15979    | 1     | 17481 | 3244s → 3122s    |
| roslyn (csharp) | 67525  | 47182   | 5141     | 19    | 6884  | 126s → 117s      |

### 実装した変更の要約

1. **計測**: public `ParseTimings` + Bench 内訳 / LL 率 / `compile_ms`
2. **ANTLR**: Build を最終 1 回に、`Cancelable*ErrorStrategy` で `ParseTimeout` / cancel を `Sync` に接続
3. **tree-sitter**: `LibraryImport` + `SuppressGCTransition`、cursor 走査、`has_error` 枝刈り
4. **CLI**: 言語遅延登録、`RegisterAlias` の stale クリア、R2R 計測のみ
5. **マッチング等**: `$$$` undo log、env 再利用、LINQ 削減、`SourceFile.Hash` 遅延、`MaxResults` 早期打ち切り

### 残課題

- ANTLR の LL フォールバック 67%（commons-lang / Java）が最大ボトルネック。文法・予測モード側の改善が次の主戦場。
- tree-sitter の build（arena 変換）はまだ native parse と同オーダー。さらなる削減はバッチ取得や arena 直書きが候補。
- `dotnet tool` への R2R / RID 別配布は配布方針の判断が必要（今回は未採用）。

## 対応結果 2: ANTLR の LL フォールバック削減（Java）

### 診断モード

Bench に `--antlr-diagnose` と `--tree-hashes` を追加した（`tools/AstGrepAntlr.Bench/AntlrDiagnostics.cs`）。
`IAntlrLanguage` を直接使うので、組み込みの全 ANTLR 言語と自作 `.g4`（`InterpretedLanguage`）で動く。

```sh
# SLL の中断箇所、LL でもエラーか、判断ごとの予測時間 / LL_Fallback / LL_MaxLook
dotnet run -c Release --project tools/AstGrepAntlr.Bench -- -l java --backend antlr --antlr-diagnose --top 15 --max-files 2000 .difftest/corpus/commons-lang/src/main/java
# ファイルごとの木のハッシュ（文法・TreeBuilder 変更の前後で突き合わせる）
dotnet run -c Release --project tools/AstGrepAntlr.Bench -- -l java --backend antlr --tree-hashes before.tsv --max-files 100000 .difftest/corpus
```

### 原因と修正

- SLL の中断 177 件はすべて `identifier`（`=` / `;` / `(` の手前）で起きていた。`classType` の `( ... )+` が
  `String s` の `s` まで型として読むため。LL 側でも `classType` の判断（d69）が予測時間の 1 位だった。
- `classType` の `+` を外した（`grammars/java/README.md` に記録）。`.difftest/corpus` の Java 1387 ファイルで
  木のハッシュは修正前と全件一致。
- 残り 4 件は interface の default メソッド内のラムダ（`->`）で SLL が中断するもの。件数が少ないため見送り。
  残る LL_Fallback の上位は `expression` / `interfaceBodyDeclaration`（左再帰と曖昧さ由来）。

### ツリー変換

- `TreeBuilder`: トークン種別ごとに型名と `IsNamed` を言語単位でキャッシュ（全言語の `IsNamedToken` が `token.Type`
  だけで決まることを確認済み。公開インターフェースは不変）。
- `SyntaxTreeAssembler`: 別名も子ルールも持たないノードは、種類名ごとに共有する 1 要素配列を使う。`AddUnique` の LINQ を削除。
- HEAD のビルドと木のハッシュを突き合わせ、Java 1387 ファイルで全件一致。

### Bench（Release、commons-lang 264 files、5 回の最小値）

| 対象                | 項目     |        変更前 |     変更後 |
| ------------------- | -------- | ------------: | ---------: |
| Java / ANTLR        | parse_ms |          3710 |       2053 |
| Java / ANTLR        | ll_ms    |         ~2500 |      30–70 |
| Java / ANTLR        | LL 率    | 177/264 (67%) | 4/264 (2%) |
| Java / ANTLR        | build_ms |           501 |        418 |
| Java / tree-sitter  | build_ms |           237 |        224 |
| C# / Roslyn（1587） | build_ms |          1259 |       1069 |

SLL が最後まで進むようになった分、Java / ANTLR の SLL 時間は ~80ms → ~1000–1300ms に増えた（初回の DFA 構築）。
それでも LL の ~2500ms が消えた分、解析全体では約 45% 短くなった。

### difftest（Java / ANTLR）

集計は変更前と完全に一致（same 30317 / only-sg 4705 / only-ag4 11162 / range 0 / meta 4715）。
ag4 の合計時間は 381.7s → 209.6s（ast-grep 比 19.7x → 9.8x）。

### 他の ANTLR 言語（診断のみ、文法は未修正）

| 言語（コーパス、最大 300 files） |  SLL 失敗 | うち LL でエラーなし | LL_Fallback 上位                                                                  |
| -------------------------------- | --------: | -------------------: | --------------------------------------------------------------------------------- |
| Python（209）                    |    6 (3%) |                    0 | `atom_expr`（66）                                                                 |
| PL/SQL（68）                     |  26 (38%) |                    9 | `general_element` / `type_spec` / `concatenation`                                 |
| SQLite（68、PL/SQL の .sql）     |  66 (97%) |                    0 | 方言違いの入力で構文エラーになるため参考値                                        |
| C++（300）                       | 243 (81%) |                  124 | `theTypeName`（20144）/ `statement` / `nestedNameSpecifier` / `postfixExpression` |

C++ は LL 時間（プロファイル込み約 99s）の大半が `statement` / `postfixExpression` / `theTypeName` の判断に集中しており、次の候補。
