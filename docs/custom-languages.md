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

述語やアクションをそのまま使いたい場合は、`sgconfig.yml` ではなく、文法を C# のコードとして ag4 本体か自分の .NET プロジェクトに組み込みます。手順は [10 章](#10-述語アクションを使う文法を組み込む) を参照してください。

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

---

## 10. 述語・アクションを使う文法を組み込む

`sgconfig.yml` の `customLanguages` はインタプリタで動くため、`.g4` の隣に `.cs` を置いても実行されません（[3.2](#32-使えない文法機能)）。述語・アクション・`superClass` の基底クラスを使うには、ANTLR が生成した C# パーサと基底クラスを**ビルド時にコンパイルして**組み込みます。組み込み言語の Python・PL/SQL・C++・Java も、この方式で述語付きの grammars-v4 文法を動かしています。

### 10.1 どちらの方法を選ぶか

|                                 | 方法 A: ag4 本体に組み込む | 方法 B: 自分の .NET プロジェクトに組み込む |
| ------------------------------- | -------------------------- | ------------------------------------------ |
| ag4 本体の変更                  | 必要（フォークして修正）   | 不要                                       |
| `ag4 run` / `scan` / `debug`    | 使える                     | 使えない                                   |
| `ag4 mcp`                       | 使える                     | 使えない                                   |
| NativeAOT（リリース版バイナリ） | 使える                     | 自分のアプリ次第（AOT でもビルドできる）   |
| `ag4_native`（C ABI）           | 使える                     | 使えない                                   |
| .NET から `Searcher` で呼ぶ     | 使える                     | 使える                                     |

CLI や MCP から使いたいなら方法 A、自分の .NET アプリやツールの中で検索できれば十分なら方法 B が手軽です。

どちらの方法でも、文法側の準備は同じです。以下では、`calc` を例に「`pi` と `e` への代入だけを別の規則 `constAssign` として解析する」述語を入れた文法を使います。

```antlr
// CalcParser.g4
parser grammar CalcParser;

options { tokenVocab = CalcLexer; superClass = CalcParserBase; }

program     : statement* EOF ;
statement   : constAssign
            | assign
            ;
// 代入先が予約された定数名のときだけ選ばれる
constAssign : { IsReserved(CurrentToken.Text) }? ID ASSIGN expr SEMI ;
assign      : ID ASSIGN expr SEMI ;
expr        : expr PLUS expr
            | NUMBER
            | ID
            ;
```

```csharp
// CSharp/CalcParserBase.cs
using Antlr4.Runtime;

namespace CalcExt.Calc; // csproj の <Package> と同じ名前空間にする

public abstract class CalcParserBase : Parser
{
    protected CalcParserBase(ITokenStream input)
        : base(input) { }

    protected CalcParserBase(ITokenStream input, TextWriter output, TextWriter errorOutput)
        : base(input, output, errorOutput) { }

    // Names that must not be reassigned.
    protected static bool IsReserved(string name) => name is "pi" or "e";
}
```

基底クラスには、ANTLR が生成するパーサが呼ぶ 2 つのコンストラクタ（`ITokenStream` だけを受け取るものと、出力先も受け取るもの）を用意してください。

### 10.2 方法 A: ag4 本体に組み込む

既存の組み込み言語と同じ手順です。迷ったら Python（`grammars/python/`、`src/AstGrepAntlr.Languages/Python/`）か PL/SQL の構成をそのまま真似してください。

#### 手順 1. 文法と基底クラスを置く

grammars-v4 と同じ配置にします。

```text
grammars/calc/
  CalcLexer.g4
  CalcParser.g4
  CSharp/
    CalcParserBase.cs
  LICENSE        # 文法のライセンス
  README.md      # 出典（取得元の URL とコミット）
```

#### 手順 2. 基底クラスの名前空間を合わせる

ANTLR が生成するクラスは、csproj の `<Package>` で指定した名前空間に入ります。`superClass` で指定した基底クラスも同じ名前空間に置いてください。grammars-v4 から持ってきた基底クラスは、名前空間を書き換えて、冒頭にその旨を書いておきます。

```csharp
// grammars/python/CSharp/Python3LexerBase.cs（実際のファイル）
// Vendored from grammars-v4 python/python3/CSharp/Python3LexerBase.cs (MIT).
// Namespace adjusted to match Antlr4BuildTasks Package setting.
...
namespace AstGrepAntlr.Languages.Python;

public abstract class Python3LexerBase : Lexer
```

#### 手順 3. csproj に登録する

`src/AstGrepAntlr.Languages/AstGrepAntlr.Languages.csproj` の `<ItemGroup>` に、`.g4` を `<Antlr4>` として、基底クラスを `<Compile>` として追加します。ビルド時に Antlr4BuildTasks が C# を生成します（`Antlr4UseCSharpGenerator` が有効なので、ビルドに Java は要りません）。

```xml
<Antlr4 Include="..\..\grammars\calc\CalcLexer.g4">
  <Generator>MSBuild:Compile</Generator>
  <Listener>false</Listener>
  <Visitor>false</Visitor>
  <Package>AstGrepAntlr.Languages.Calc</Package>
</Antlr4>
<Antlr4 Include="..\..\grammars\calc\CalcParser.g4">
  <Generator>MSBuild:Compile</Generator>
  <Listener>false</Listener>
  <Visitor>false</Visitor>
  <Package>AstGrepAntlr.Languages.Calc</Package>
</Antlr4>
<Compile Include="..\..\grammars\calc\CSharp\CalcParserBase.cs" Link="Calc\CalcParserBase.cs" />
```

lexer に `superClass` があれば、`XxxLexerBase.cs` も同じように `<Compile>` に追加します。

#### 手順 4. 言語クラスを書く

`src/AstGrepAntlr.Languages/Calc/CalcLanguage.cs` に `IAntlrLanguage` を実装します。いちばん小さい実例は `src/AstGrepAntlr.Languages/Sqlite/SqliteLanguage.cs` です。

```csharp
using Antlr4.Runtime;
using AstGrepAntlr.Language;
using AstGrepAntlr.Languages.Calc;

namespace AstGrepAntlr.Languages;

public sealed class CalcLanguage : IAntlrLanguage
{
    public string Id => "calc";
    public IReadOnlyList<string> Extensions { get; } = [".calc"];
    public string StartRule => "program";
    public IReadOnlyList<string> PatternRules { get; } = ["assign", "statement", "expr", "program"];
    public string MetaVarPlaceholderTemplate => "__MV{0}__";
    public IReadOnlyList<string> MetaVarVariants { get; } = ["{0}", "{0};"];
    public bool IgnoreCase => false;
    public TimeSpan ParseTimeout => TimeSpan.FromSeconds(30);
    public long MaxFileBytes => 4 * 1024 * 1024;
    public IReadOnlyList<string> RuleNames => CalcParser.ruleNames;
    public IVocabulary Vocabulary => CalcLexer.DefaultVocabulary;

    public Lexer CreateLexer(ICharStream stream) => new CalcLexer(stream);

    public Parser CreateParser(ITokenStream tokens) => new CalcParser(tokens);

    // startRule・PatternRules・ルールの entry で使う規則を、生成されたメソッドに振り分ける
    public ParserRuleContext InvokeRule(Parser parser, string ruleName)
    {
        var p = (CalcParser)parser;
        return ruleName.ToLowerInvariant() switch
        {
            "program" => p.program(),
            "statement" => p.statement(),
            "constassign" => p.constAssign(),
            "assign" => p.assign(),
            "expr" => p.expr(),
            _ => throw new ArgumentException($"Unknown calc rule '{ruleName}'."),
        };
    }

    // Symbol literals such as '=' or '+' are unnamed (like tree-sitter anonymous nodes).
    public bool IsNamedToken(IToken token, IVocabulary vocabulary)
    {
        var literal = vocabulary.GetLiteralName(token.Type);
        return string.IsNullOrEmpty(literal) && !string.IsNullOrEmpty(vocabulary.GetSymbolicName(token.Type));
    }
}
```

`InvokeRule` は `ruleName.ToLowerInvariant()` で比較しているので、switch の左辺は**小文字**で書きます。

各メンバーは、[4 章](#4-sgconfigyml-に登録する)の `sgconfig.yml` のキーに対応します。

| メンバー                     | `sgconfig.yml` のキー   | 補足                                                                                                  |
| ---------------------------- | ----------------------- | ----------------------------------------------------------------------------------------------------- |
| `Id`                         | 言語 ID（マップのキー） | `-l` や YAML ルールの `language` で使う                                                               |
| `Extensions`                 | `extensions`            | こちらは**ドット付き**（`".calc"`）                                                                   |
| `StartRule`                  | `startRule`             |                                                                                                       |
| `PatternRules`               | `patternRules`          | 具体的な規則から順に並べる（[4.2](#42-patternrules順番が重要)）                                       |
| `MetaVarPlaceholderTemplate` | `metaVarPlaceholder`    |                                                                                                       |
| `MetaVarVariants`            | `metaVarVariants`       |                                                                                                       |
| `IgnoreCase`                 | `ignoreCase`            |                                                                                                       |
| `ParseTimeout`               | —                       | 1 ファイルの解析タイムアウト                                                                          |
| `MaxFileBytes`               | —                       | これより大きいファイルは読み飛ばす                                                                    |
| `IsNamedToken`               | —                       | `false` を返したトークンは `unnamed` 扱いになり、`kind` で指定できない（[5 章](#5-木の形を確認する)） |

#### 手順 5. 言語を登録する

`src/AstGrepAntlr.Languages/BuiltinLanguages.cs` の `RegisterFixed` に追加します。別名が要るなら `RegisterAlias` も追加します。

```csharp
private static void RegisterFixed(LanguageRegistry registry)
{
    registry.RegisterLazy("sqlite", static () => new SqliteLanguage());
    registry.RegisterLazy("plsql", static () => new PlSqlLanguage());
    registry.RegisterAlias("pl/sql", "plsql");
    registry.RegisterLazy("calc", static () => new CalcLanguage()); // 追加
}
```

#### 手順 6. ビルドして確認する

```bash
dotnet build
dotnet run --project src/AstGrepAntlr.Cli -- debug parse -l calc --code 'pi = 3; x = 1;'
dotnet run --project src/AstGrepAntlr.Cli -- debug pattern -l calc -p '$A = $B;'
dotnet test
```

`debug parse` で、`pi = 3;` が `constAssign`、`x = 1;` が `assign` になっていれば述語が効いています。テストは `tests/AstGrepAntlr.Tests/PlSqlParseTests.cs` や `PythonParseTests.cs` を参考に追加してください。

NativeAOT 版でも動くことは、リリースと同じコマンドで確認できます。

```bash
dotnet publish src/AstGrepAntlr.Cli/AstGrepAntlr.Cli.csproj -c Release -r win-x64 -p:PublishAot=true
```

#### 手順 7. 付随する更新

- `README.md` / `README.en.md` の言語表（Id・拡張子・既定バックエンド）
- `THIRD-PARTY-NOTICES.md`（文法と基底クラスのライセンス）

### 10.3 方法 B: 自分の .NET プロジェクトに組み込む

ag4 本体は変更せず、ライブラリとして使います。文法・基底クラス・言語クラスは方法 A と同じものを、自分のプロジェクトに置きます（名前空間は自分のものに変えます）。

```xml
<!-- CalcExt.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Antlr4UseCSharpGenerator>true</Antlr4UseCSharpGenerator>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Antlr4.Runtime.Standard" Version="4.13.1" />
    <PackageReference Include="Antlr4BuildTasks" Version="12.10.0" PrivateAssets="all" />
    <!-- NuGet なら PackageReference、ソースから使うなら ProjectReference -->
    <PackageReference Include="AstGrepAntlr.Core" Version="..." />
    <PackageReference Include="AstGrepAntlr.Languages" Version="..." />
  </ItemGroup>
  <ItemGroup>
    <Antlr4 Include="Grammars/CalcLexer.g4">
      <Listener>false</Listener>
      <Visitor>false</Visitor>
      <Package>CalcExt.Calc</Package>
    </Antlr4>
    <Antlr4 Include="Grammars/CalcParser.g4">
      <Listener>false</Listener>
      <Visitor>false</Visitor>
      <Package>CalcExt.Calc</Package>
    </Antlr4>
  </ItemGroup>
</Project>
```

SDK 形式のプロジェクトでは、プロジェクト配下の `.cs`（`Grammars/CSharp/CalcParserBase.cs` を含む）は自動でコンパイルされるので、`<Compile>` は要りません。

言語を登録してから `Searcher` を呼びます。

```csharp
using AstGrepAntlr.Languages;
using AstGrepAntlr.Search;
using CalcExt;

var registry = BuiltinLanguages.CreateRegistry(); // 組み込み言語も使えるようにする
registry.Register(new CalcLanguage());

var matches = new Searcher().RunPattern(
    "$A = $B;",
    new SearchOptions { Registry = registry, LanguageId = "calc", Paths = ["src"] }
);
foreach (var m in matches)
{
    Console.WriteLine($"{m.File}:{m.StartLine}: {m.Text}");
}
```

`pi = 3;`、`x = 1;`、`y = x + 2;` を含むファイルに対してこのコードを実行すると、`x = 1;` と `y = x + 2;` だけがヒットします。パターン `$A = $B;` は `PatternRules` の先頭の `assign` として解析され、`pi = 3;` は述語によって `constAssign` になっているためです。

### 10.4 注意点

- **述語はパターンの解析でも評価されます。** パターン中のメタ変数は `__MV0__` などのプレースホルダに置き換えてから解析されるので、上の例の `IsReserved` はメタ変数の位置では常に偽になります。述語で分かれた規則を狙うには、次のどちらかにします。
  - YAML ルールで `kind: constAssign` を使う
  - 述語の条件を満たす具体的な文字列を書き、`entry` でその規則を指定する（例: `pattern: { context: "pi = $B;", entry: constAssign }`）。`entry` を付けないと、`PatternRules` の先頭の `assign` として解析されてヒットしません

  なお、`$A = $B;` に `entry: constAssign` を付けると、述語が偽になって解析に失敗し、**エラーにならずに**次の候補の `assign` として解析されます。意図した規則で解析されたかは `ag4 debug pattern` の `entry:` 行で確認してください。

- **規則の途中の述語が偽になっても、ノードは消えません。** `statement : name=ID { Check($name.text) }? ...` のように、規則の途中で入力を「拒否する」ための述語は、構文エラー（`rule statement failed predicate: ...`）として記録されます。そのあとエラー回復で解析が続くので、ノードは木に残り、照合の対象にもなります。照合の結果を変えたいなら、上の例のように**代替規則の先頭に述語を置いて、どの規則として解析するかを選ばせる**書き方にしてください。
- **ランタイムのバージョン。** ag4 は `Antlr4.Runtime.Standard` 4.13.1 を使っています。grammars-v4 の C# 用基底クラスの中には古いランタイム API を前提にしたものがあるので、ビルドエラーが出たら現行 API に合わせて直してください。
- **性能。** 述語が多い文法や、予測で述語を何度も評価する文法は遅くなることがあります。組み込みの C++（`grammars/cpp/CSharp/CPP14ParserBase.cs`）がその例です。数値は [ast-grep-vs-ag4.html](../ast-grep-vs-ag4.html) の C++ ANTLR を参照してください。
