# 独自言語の追加（ANTLR4 文法）

ag4 は ANTLR4 の文法（`.g4`）、またはそこから生成した `.interp` を `sgconfig.yml` に登録するだけで、任意の言語を構造検索の対象にできます。C# のコード生成やビルドは不要で、文法は実行時にインタプリタで動きます。

登録した言語では、組み込み言語と同じく次の機能が使えます。

- `ag4 run`（パターン検索・`--rewrite` による書き換え）
- `ag4 scan`（YAML ルール）
- `ag4 debug parse` / `ag4 debug pattern`
- `ag4 mcp`（AI エージェント向け MCP サーバ）

動作するサンプルは [samples/dsl](../samples/dsl) にあります（四則演算の小さな言語 `calc`）。

---

## 1. 5 分で試す（同梱サンプル）

```bash
cd samples/dsl

# パターン検索：自己代入を探す
ag4 run -l calc -p '$A = $A;' .
# sample.calc
# 1:1:x = x;

# YAML ルール（rules/no-assign-self.yml）で scan
ag4 scan .

# 書き換え（--apply を付けるとファイルに反映）
ag4 run -l calc -p '$A = $B + $C;' --rewrite '$A = $C + $B;' .
```

`sgconfig.yml` は検索パスから上方向に自動探索されます。別の場所にある場合は `-c path/to/sgconfig.yml` で指定します。

---

## 2. 全体の流れ

1. 文法を用意する（lexer / parser を分けた `.g4`、または生成済みの `.interp`）
2. `sgconfig.yml` の `customLanguages` に登録する
3. `ag4 debug parse` で木の形（kind 名）を確認する
4. `ag4 debug pattern` でパターンが意図どおり解釈されるか確認する
5. パターンや YAML ルールを書いて `run` / `scan` する

---

## 3. 文法を用意する

### 3.1 lexer と parser を分ける（必須）

**lexer grammar と parser grammar を別ファイルに分けてください。** 結合文法（1 ファイルに `grammar Foo;` と書く形式）には現在対応していません。指定すると `ag4: Parser .interp not produced.` で失敗します。

```antlr
// CalcLexer.g4
lexer grammar CalcLexer;

PLUS  : '+';
ASSIGN: '=';
SEMI  : ';';
NUMBER: [0-9]+ ('.' [0-9]+)?;
ID    : [a-zA-Z_][a-zA-Z0-9_]*;
WS    : [ \t\r\n]+ -> skip;
```

```antlr
// CalcParser.g4
parser grammar CalcParser;

options { tokenVocab = CalcLexer; }

program   : statement* EOF ;
statement : ID ASSIGN expr SEMI ;
expr      : expr PLUS expr
          | NUMBER
          | ID
          ;
```

結合文法を分けるときは、トークン規則（大文字で始まる規則）を `XxxLexer.g4` に移し、parser 側に `options { tokenVocab = XxxLexer; }` を書きます。parser 規則の中に直接書いた `'+'` のようなリテラルも、lexer 側にトークンとして定義しておきます。

ファイル名は `〜Lexer`、`〜Parser` で終わる名前にしてください。ag4 は生成された `*Lexer.interp` と `*Parser.interp` をファイル名で見分けます。

### 3.2 使えない文法機能

文法は ANTLR の `LexerInterpreter` / `ParserInterpreter` で解釈するため、ターゲット言語のコードに依存する機能は**実行されません**。

| 機能                                                                    | 扱い                                 |
| ----------------------------------------------------------------------- | ------------------------------------ |
| アクション `{ ... }`                                                    | 無視される                           |
| 意味述語 `{ ... }?`                                                     | 評価されない（常に真として扱われる） |
| `superClass` などで指定した基底クラスのコード                           | 使われない                           |
| lexer のモード（`mode` / `pushMode`）・チャネル（`-> channel(HIDDEN)`） | 使える                               |

[grammars-v4](https://github.com/antlr/grammars-v4) の文法を使うときは、述語やアクションに依存していないか確認してください。依存している場合は、述語なしで成り立つように規則を書き換える必要があります（例：文脈によって変わるキーワード判定を、トークン定義の順序で表現する）。

### 3.3 `.g4` と `.interp` のどちらを指定するか

| 指定するもの | 必要な環境                         | 向いている場面                     |
| ------------ | ---------------------------------- | ---------------------------------- |
| `.g4`        | JDK と `antlr-4.13.2-complete.jar` | 文法を編集しながら試すとき         |
| `.interp`    | 不要                               | CI や、Java がないマシンに配るとき |

`.g4` を指定した場合、初回実行時に ag4 が `java -jar antlr-*.jar` で `.interp` を生成し、次の場所にキャッシュします。

- Windows: `%LOCALAPPDATA%\ag4\cache\<hash>\`
- Linux: `~/.local/share/ag4/cache/<hash>/`
- macOS: `~/Library/Application Support/ag4/cache/<hash>/`

`<hash>` は文法ファイルのパスと内容から計算されるため、`.g4` を編集すると自動で作り直されます。

ANTLR jar は次の順に探します。

1. `--antlr-jar` オプション
2. 環境変数 `ANTLR_JAR`
3. `sgconfig.yml` のあるディレクトリとその祖先にある `tools/antlr-4.13.2-complete.jar`
4. カレントディレクトリとその祖先にある `tools/antlr-4.13.2-complete.jar`
5. ag4 本体と同じディレクトリ（または `tools/`）

`.interp` を事前に生成するには、次のコマンドを実行します（サンプルと同じ手順です）。

```bash
java -jar tools/antlr-4.13.2-complete.jar -Dlanguage=CSharp -Xexact-output-dir -o samples/dsl \
  samples/dsl/CalcLexer.g4 samples/dsl/CalcParser.g4
```

生成された `CalcLexer.interp` と `CalcParser.interp` を `grammar` に並べて指定します。`.tokens` や `.cs` は使いません。

---

## 4. `sgconfig.yml` に登録する

```yaml
customLanguages:
  calc: # 言語 ID（-l calc や YAML ルールの language: calc で使う。大文字小文字は区別しない）
    grammar: [CalcLexer.g4, CalcParser.g4]
    extensions: [calc]
    startRule: program
    patternRules: [statement, expr, program]
    metaVarPlaceholder: "__MV{0}__"
    metaVarVariants: ["{0}", "{0};"]
    ignoreCase: false
```

| キー                 | 必須 | 既定値        | 説明                                                                                  |
| -------------------- | ---- | ------------- | ------------------------------------------------------------------------------------- |
| `grammar`            | ○    | —             | `.g4` 2 つ、または `.interp` 2 つ。相対パスは `sgconfig.yml` のあるディレクトリが基準 |
| `extensions`         |      | `[]`          | 対象ファイルの拡張子（ドットなし）                                                    |
| `startRule`          |      | `program`     | ファイル全体を解析するときの開始規則                                                  |
| `patternRules`       |      | `[startRule]` | パターンを解析するときに試す規則。**先頭から順に**試す                                |
| `metaVarPlaceholder` |      | `__MV{0}__`   | メタ変数を置き換える仮の文字列。`{0}` には連番が入る                                  |
| `metaVarVariants`    |      | `["{0}"]`     | 解析に失敗したときに試す、プレースホルダの書き方の候補                                |
| `ignoreCase`         |      | `false`       | 大文字小文字を区別しない言語なら `true`                                               |

言語は実際に使われたときに初めて読み込まれます。登録した言語が多くても、起動時間は延びません。

### 4.1 `startRule`

ファイル全体を表す規則を指定します。通常は末尾に `EOF` を持つ規則（`program : statement* EOF ;` など）です。

### 4.2 `patternRules`（順番が重要）

パターン文字列は、ここに並べた規則で先頭から順に解析され、**入力を最後まで読み切れた最初の規則**が採用されます。

小さい単位（具体的な規則）から先に並べてください。`program : statement* ;` のように何でも受け付ける規則を先頭に置くと、`$A = $A;` が「文 1 つ」ではなく「文の並び」として解釈され、照合の単位が変わってしまいます。

```yaml
patternRules: [statement, expr, program] # ○ 具体的なものから
patternRules: [program, statement, expr] # × 何でも program になる
```

1 つのパターンだけ別の規則で解析させたいときは、ルール側で `entry` を指定します（[6.3](#63-解析規則を指定する)）。

### 4.3 `metaVarPlaceholder`

パターン中の `$A` や `$$$ARGS` は、解析の前に `__MV0__`、`__MV1__` のような文字列に置き換えられます。この文字列は、**その言語の lexer で 1 つのトークン（ふつうは識別子）として読まれる**必要があります。

識別子に `_` を使えない言語では、使える形に変えます。

```yaml
metaVarPlaceholder: "Ag4Mv{0}" # 組み込みの PL/SQL で使っている形
```

キーワードと衝突せず、ソースコードに普通は出てこない文字列を選んでください。

### 4.4 `metaVarVariants`

メタ変数の位置に識別子 1 つを置くだけでは文法上成り立たない場合に備えて、置き換え方の候補を並べます。`{0}` がプレースホルダに置き換わります。

ag4 はメタ変数ごとに候補を組み合わせて解析を試します（最大 4096 通り）。

| 候補        | 用途の例                                            |
| ----------- | --------------------------------------------------- |
| `"{0}"`     | 式・識別子の位置（基本）                            |
| `"{0};"`    | 文の位置に `$STMT` を書きたいとき                   |
| `""`        | 空でもよい位置（`$$$` が 0 個にマッチする場合など） |
| `"int {0}"` | 型と名前が必要な位置（Java の組み込み定義の例）     |

候補を増やすほど書けるパターンは増えますが、解析を試す回数も増えます。必要なものだけにしてください。

### 4.5 `ignoreCase`

SQL のように大文字小文字を区別しない言語では `true` にします。現在の実装では、この設定はファイルの事前絞り込み（パターン中のリテラルを含むかどうかの判定）にだけ使われます。`false` のままだと、`select` と `SELECT` のように表記が違うファイルが照合の前に除外されることがあります。

---

## 5. 木の形を確認する

ルールを書く前に、実際のコードがどんな木になるかを確認します。

```bash
ag4 debug parse -l calc --code 'x = 1 + 2;'
```

```text
program [0,10]
  statement [0,10]
    ID [0,1] 'x'
    ASSIGN [2,3] unnamed '='
    expr [4,9]
      expr [4,5]
        NUMBER [4,5] '1'
      PLUS [6,7] unnamed '+'
      expr [8,9]
        NUMBER [8,9] '2'
    SEMI [9,10] unnamed ';'
  EOF [10,10] ''
```

- **kind 名は文法の規則名そのまま**です（`program`、`statement`、`expr`）。トークンは lexer の規則名になります（`ID`、`NUMBER`）。
- `unnamed` と表示されるのは、短い記号のリテラルトークン（`'='`、`'+'`、`';'` など）です。tree-sitter の anonymous ノードと同じ扱いで、**`kind:` や `has` / `inside` の対象にはなりません**。記号を条件にしたいときは `pattern` を使います。
- 角括弧の中は文字オフセット（0 始まり、終端は含まない）です。

ファイルを解析するときは、`--code` の代わりにファイルパスを渡します。

---

## 6. パターンとルールを書く

### 6.1 パターンの解釈を確認する

```bash
ag4 debug pattern -l calc -p '$X + $Y'
```

```text
entry: expr
expr [0,17]
  expr [0,7] metavar=$X
    ID [0,7] '__MV0__'
  PLUS [8,9] unnamed '+'
  expr [10,17] metavar=$Y
    ID [10,17] '__MV1__'
```

`entry` に、採用された規則（`patternRules` のどれか）が表示されます。`metavar=` が付いたノードに、メタ変数が割り当てられています。

パターンを解析できない場合は、次のエラーになります。

```text
ag4: Failed to parse pattern as any of [statement, expr, program]. Try `ag4 debug pattern` or set pattern.entry.
```

このときは次の点を確認してください。

- パターンは、`patternRules` のどれか 1 つの規則で**最後まで**読み切れる断片になっているか（文の規則に `;` が必要なら、パターンにも `;` が必要です）
- プレースホルダがその位置で文法上許されるトークンか（[4.3](#43-metavarplaceholder)、[4.4](#44-metavarvariants)）

### 6.2 YAML ルール

組み込み言語と同じ書き方で、`language` に言語 ID を指定します。

```yaml
# rules/no-assign-self.yml
id: no-assign-self
language: calc
severity: warning
message: Self-assignment is redundant
rule:
  pattern: $A = $A;
```

`sgconfig.yml` に `ruleDirs` を書いておくと、`ag4 scan` がそのディレクトリのルールをまとめて読み込みます。

```yaml
ruleDirs:
  - rules
customLanguages:
  calc: ...
```

`kind`、`has`、`inside`、`all` / `any` / `not` なども使えます。

```yaml
# 文の中にある割り算
id: div-in-statement
language: calc
severity: error
message: Division found
rule:
  pattern: $A / $B
  inside:
    kind: statement
```

```yaml
# 数値リテラルを直接の子に持つ式
id: numeric-expr
language: calc
severity: hint
message: Numeric literal
rule:
  kind: expr
  has:
    kind: NUMBER # 名前付きトークンは kind で指定できる（記号トークンは不可）
```

### 6.3 解析規則を指定する

パターンの断片を独立して書けないときは、`context` に周りのコードを含めて書き、`selector` で照合に使う部分木の kind を指定します。

```yaml
rule:
  pattern:
    context: $A = $A;
    selector: statement
```

`patternRules` の順番とは関係なく特定の規則で解析させたいときは、`entry` を指定します。

```yaml
rule:
  pattern:
    context: "1"
    entry: expr
```

---

## 7. 実行方法のまとめ

```bash
# パターン検索（sgconfig.yml は検索パスから上方向に自動探索）
ag4 run -l calc -p '$A = $A;' src/

# sgconfig.yml を明示
ag4 run -c path/to/sgconfig.yml -l calc -p '$A = $A;' src/

# ANTLR jar を明示（.g4 を指定している場合）
ag4 run --antlr-jar /opt/antlr/antlr-4.13.2-complete.jar -l calc -p '$A = $A;' src/

# JSON 出力・書き換え
ag4 run -l calc -p '$A = $A;' --json src/
ag4 run -l calc -p '$A = $B + $C;' --rewrite '$A = $C + $B;' --apply src/

# ルールで scan（severity が --error-on 以上のマッチがあれば終了コード 1）
ag4 scan -c sgconfig.yml src/
ag4 scan -r rules/no-assign-self.yml --error-on warning src/

# MCP サーバ（sgconfig.yml の customLanguages も読み込まれる）
ag4 mcp --root .
```

文字コードの指定（`--encoding`、`sgconfig.yml` の `encoding`）も、組み込み言語と同じように使えます。

---

## 8. 制限と性能

| 項目                         | 値・挙動                                                           |
| ---------------------------- | ------------------------------------------------------------------ |
| 1 ファイルの最大サイズ       | 4 MiB（超えたファイルは読み飛ばす）                                |
| 1 ファイルの解析タイムアウト | 30 秒                                                              |
| 構文エラー                   | SLL で失敗したら LL で再解析し、エラー回復しながら最後まで木を作る |
| 解析速度                     | インタプリタ実行のため、生成コードの ANTLR パーサより遅い          |

ANTLR の予測用 DFA は、同じプロセスで解析を繰り返すほど育って速くなります。大量のファイルを何度も検索するなら、毎回 CLI を起動するより MCP サーバや .NET ライブラリ（`Searcher`）として常駐させる方が速くなります。

文法が曖昧で予測が重い場合（左再帰の深い式、長い先読みが必要な規則など）は、解析時間が大きく伸びます。遅いときは、`ag4 debug parse` で小さな入力の解析時間を見ながら文法を整理してください。

---

## 9. トラブルシューティング

| 症状・メッセージ                          | 原因と対処                                                                                                                                                     |
| ----------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Parser .interp not produced.`            | 結合文法を指定している。lexer と parser の `.g4` に分ける（[3.1](#31-lexer-と-parser-を分ける必須)）                                                           |
| `ANTLR tool jar not found. ...`           | jar が見つからない。`--antlr-jar` か `ANTLR_JAR` を指定するか、`tools/` に置く。表示される `Searched:` の一覧で探した場所を確認できる                          |
| `antlr tool failed: ...`                  | 文法にエラーがある。`java -jar antlr-4.13.2-complete.jar` を直接実行してエラーを確認する                                                                       |
| `Failed to start java.`                   | `java` に PATH が通っていない。JDK を入れるか、`.interp` を事前生成して指定する                                                                                |
| `Failed to parse pattern as any of [...]` | パターンがどの規則でも読み切れない（[6.1](#61-パターンの解釈を確認する)）                                                                                      |
| `Unknown rule '...' for language '...'`   | `startRule` / `patternRules` / `entry` に文法に無い規則名を書いている                                                                                          |
| ヒットしない（エラーなし）                | `extensions` が対象ファイルに合っていない、記号トークンを `kind` で指定している、`patternRules` の順番でパターンが別の規則として解釈されている、などを確認する |
| 言語 ID が見つからない                    | `sgconfig.yml` が自動探索されていない。`-c` で明示する                                                                                                         |
