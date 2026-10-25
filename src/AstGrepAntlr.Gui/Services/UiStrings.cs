using System.Globalization;

namespace AstGrepAntlr.Gui.Services;

/// <summary>UI text for one language. Switching language swaps the whole instance.</summary>
public sealed class UiStrings
{
    public required string Code { get; init; }
    public required string SwitchLanguage { get; init; }
    public required string Root { get; init; }
    public required string Browse { get; init; }
    public required string BrowseFile { get; init; }
    public required string Mode { get; init; }
    public required string ModeAst { get; init; }
    public required string ModeRule { get; init; }
    public required string ModeText { get; init; }
    public required string ModeRegex { get; init; }
    public required string Language { get; init; }
    public required string Strictness { get; init; }
    public required string Backend { get; init; }
    public required string CaseSensitive { get; init; }
    public required string WholeWord { get; init; }
    public required string RuleSource { get; init; }
    public required string RuleSourceConfig { get; init; }
    public required string RuleSourceFile { get; init; }
    public required string RuleSourceInline { get; init; }
    public required string RulePath { get; init; }
    public required string RulePathConfigHint { get; init; }
    public required string RulePathFileHint { get; init; }
    public required string RuleIdFilter { get; init; }
    public required string Pattern { get; init; }
    public required string PatternHintAst { get; init; }
    public required string PatternHintRule { get; init; }
    public required string PatternHintText { get; init; }
    public required string PatternHintRegex { get; init; }
    public required string History { get; init; }
    public required string NoHistory { get; init; }
    public required string Presets { get; init; }
    public required string Help { get; init; }
    public required string Rewrite { get; init; }
    public required string RewriteHint { get; init; }
    public required string Advanced { get; init; }
    public required string Globs { get; init; }
    public required string GlobsHint { get; init; }
    public required string ExcludeDirs { get; init; }
    public required string ExcludeDirsHint { get; init; }
    public required string Encoding { get; init; }
    public required string ContextLines { get; init; }
    public required string MaxResults { get; init; }
    public required string NoIgnore { get; init; }
    public required string IncludeHidden { get; init; }
    public required string Incremental { get; init; }
    public required string Search { get; init; }
    public required string Cancel { get; init; }
    public required string RewritePreview { get; init; }
    public required string Export { get; init; }
    public required string Copy { get; init; }
    public required string Files { get; init; }
    public required string AllFiles { get; init; }
    public required string CodeView { get; init; }
    public required string TableView { get; init; }
    public required string ColFile { get; init; }
    public required string ColLine { get; init; }
    public required string ColColumn { get; init; }
    public required string ColText { get; init; }
    public required string ColMeta { get; init; }
    public required string ColRule { get; init; }
    public required string ColMessage { get; init; }
    public required string Find { get; init; }
    public required string FindNext { get; init; }
    public required string FindPrev { get; init; }
    public required string OpenInApp { get; init; }
    public required string CopyPath { get; init; }
    public required string Preview { get; init; }
    public required string StatusReady { get; init; }
    public required string StatusSearching { get; init; }

    /// <summary>{0}=hits, {1}=files, {2}=seconds.</summary>
    public required string StatusDone { get; init; }
    public required string StatusTruncated { get; init; }
    public required string StatusCanceled { get; init; }
    public required string StatusError { get; init; }

    /// <summary>{0}=warning count.</summary>
    public required string Warnings { get; init; }

    /// <summary>{0}=shown hits.</summary>
    public required string CodeViewTruncated { get; init; }
    public required string ExportDone { get; init; }
    public required string CopyDone { get; init; }
    public required string SettingsLoadFailed { get; init; }
    public required string RewriteTitle { get; init; }
    public required string RewriteNothing { get; init; }
    public required string RewriteApplied { get; init; }
    public required string RewriteFailed { get; init; }
    public required string RewriteAstOnly { get; init; }
    public required string Apply { get; init; }
    public required string Close { get; init; }
    public required string SelectAll { get; init; }
    public required string SelectNone { get; init; }
    public required string PreviewTitle { get; init; }
    public required string PrevHit { get; init; }
    public required string NextHit { get; init; }
    public required string HelpTitle { get; init; }
    public required string HelpPattern { get; init; }
    public required string HelpRule { get; init; }

    // Tooltips for the search-condition controls.
    public required string RootTip { get; init; }
    public required string ModeTip { get; init; }
    public required string LanguageTip { get; init; }
    public required string StrictnessTip { get; init; }
    public required string BackendTip { get; init; }
    public required string CaseSensitiveTip { get; init; }
    public required string WholeWordTip { get; init; }
    public required string RuleSourceTip { get; init; }
    public required string RulePathTip { get; init; }
    public required string RuleIdFilterTip { get; init; }
    public required string PatternTip { get; init; }
    public required string RewriteTip { get; init; }
    public required string GlobsTip { get; init; }
    public required string ExcludeDirsTip { get; init; }
    public required string EncodingTip { get; init; }
    public required string ContextLinesTip { get; init; }
    public required string MaxResultsTip { get; init; }
    public required string NoIgnoreTip { get; init; }
    public required string IncludeHiddenTip { get; init; }
    public required string IncrementalTip { get; init; }

    public static readonly UiStrings Ja = new()
    {
        Code = "ja",
        SwitchLanguage = "English",
        Root = "検索対象",
        Browse = "フォルダ…",
        BrowseFile = "ファイル…",
        Mode = "モード",
        ModeAst = "AST パターン",
        ModeRule = "YAML ルール",
        ModeText = "テキスト",
        ModeRegex = "正規表現",
        Language = "言語",
        Strictness = "厳密さ",
        Backend = "パーサー",
        CaseSensitive = "大文字小文字を区別",
        WholeWord = "単語単位",
        RuleSource = "ルール",
        RuleSourceConfig = "sgconfig.yml",
        RuleSourceFile = "ルールファイル",
        RuleSourceInline = "インライン",
        RulePath = "パス",
        RulePathConfigHint = "空欄なら検索対象から上方向に sgconfig.yml を自動検出",
        RulePathFileHint = "ルール YAML ファイル",
        RuleIdFilter = "rule-id フィルタ(正規表現)",
        Pattern = "パターン",
        PatternHintAst = "例: foo($$$ARGS)   メタ変数: $VAR / $$$ARGS / $_",
        PatternHintRule = "インライン時: ルール YAML（複数は --- 区切り）",
        PatternHintText = "検索する文字列",
        PatternHintRegex = "正規表現（.NET 構文）",
        History = "履歴",
        NoHistory = "(履歴なし)",
        Presets = "プリセット",
        Help = "ヘルプ",
        Rewrite = "置換",
        RewriteHint = "書換えテンプレート（例: bar($$$ARGS)）。空欄ならルールの fix を使用",
        Advanced = "詳細オプション",
        Globs = "対象 glob",
        GlobsHint = "例: *.java; src/**   （! で除外）",
        ExcludeDirs = "除外ディレクトリ",
        ExcludeDirsHint = "例: build; dist",
        Encoding = "文字コード",
        ContextLines = "前後の行数",
        MaxResults = "最大件数",
        NoIgnore = ".gitignore を無視",
        IncludeHidden = "隠しファイルも検索",
        Incremental = "インクリメンタル検索",
        Search = "検索 (F5)",
        Cancel = "中止 (Esc)",
        RewritePreview = "置換プレビュー…",
        Export = "エクスポート…",
        Copy = "結果をコピー",
        Files = "ファイル",
        AllFiles = "(すべて)",
        CodeView = "コード",
        TableView = "テーブル",
        ColFile = "ファイル",
        ColLine = "行",
        ColColumn = "列",
        ColText = "マッチ",
        ColMeta = "メタ変数",
        ColRule = "ルール",
        ColMessage = "メッセージ",
        Find = "検索",
        FindNext = "次へ",
        FindPrev = "前へ",
        OpenInApp = "既定のアプリで開く",
        CopyPath = "パスをコピー",
        Preview = "プレビュー",
        StatusReady = "準備完了",
        StatusSearching = "検索中…",
        StatusDone = "{0} 件 / {1} ファイル ({2:F2} 秒)",
        StatusTruncated = "（最大件数で打ち切り）",
        StatusCanceled = "中止しました",
        StatusError = "エラー: ",
        Warnings = "警告 {0} 件",
        CodeViewTruncated = "… 先頭 {0} 件のみ表示しています（全件はテーブル / エクスポートで確認できます）",
        ExportDone = "エクスポートしました: ",
        CopyDone = "クリップボードにコピーしました",
        SettingsLoadFailed = "設定を読み込めなかったため既定値で起動しました: ",
        RewriteTitle = "置換プレビュー",
        RewriteNothing = "置換対象がありません（テンプレートを入力するか、fix を持つルールで検索してください）",
        RewriteApplied = "{0} ファイルに適用しました",
        RewriteFailed = "{0} ファイルで失敗しました",
        RewriteAstOnly = "置換は AST / YAML ルールモードの結果でのみ使えます",
        Apply = "選択したファイルに適用",
        Close = "閉じる",
        SelectAll = "すべて選択",
        SelectNone = "選択解除",
        PreviewTitle = "プレビュー",
        PrevHit = "◀ 前のヒット",
        NextHit = "次のヒット ▶",
        HelpTitle = "パターン構文ヘルプ",
        HelpPattern = "パターン構文",
        HelpRule = "ルール YAML",
        RootTip = "検索するフォルダまたはファイル。フォルダの場合はサブフォルダも再帰的に検索します。",
        ModeTip =
            "AST パターン: コード片を構文木として比較し、書式や空白の違いを無視して検索\n"
            + "YAML ルール: ast-grep 形式のルール（pattern / kind / has / inside など）で検索\n"
            + "テキスト: 文字列の単純一致\n"
            + "正規表現: .NET 正規表現（^ と $ は各行の先頭・末尾に一致）",
        LanguageTip =
            "パターンを解釈する言語。\n"
            + "auto: 拡張子ごとに、パターンを解析できる言語を自動で選びます。"
            + "パターンを解析できない言語は警告に表示されます。",
        StrictnessTip =
            "パターンと対象コードを照合するときの厳しさ（例は Java）。\n\n"
            + "cst: 括弧・区切り記号・コメントまで含めて完全一致。対象側に余分な要素があると不一致\n"
            + "  例: foo($A, $B) は foo(1, /* c */ 2) に一致しない（コメントが余分）\n"
            + "  例: if ($C) { $$$B } は else 付きの if に一致しない\n\n"
            + "smart（既定）: パターンに書いた要素は一致必須。対象側にだけあるコメント・"
            + "修飾子（final など）・後続要素（else 節など）は無視\n"
            + "  例: foo($A, $B) は foo(1, /* c */ 2) に一致\n"
            + "  例: if ($C) { $$$B } は if (x) { … } else { … } にも一致\n"
            + "  例: $A + $B は 1 - 2 に一致しない（演算子が違う）\n\n"
            + "ast: 名前付きノードだけを比較し、演算子・区切り記号・コメントは無視。"
            + "対象側の後続要素も無視するため一致範囲がかなり広い\n"
            + "  例: $A + $B が 1 - 2 や a < b にも一致\n"
            + "  例: bar($X) が bar(1, 2) にも一致\n\n"
            + "relaxed: ast からさらにノード種類（kind）の照合を緩めたもの。ほとんどの場合 ast と同じ結果",
        BackendTip =
            "構文解析に使うパーサー。切り替えられるのは次の言語だけで、他の言語には影響しません。\n"
            + "java / cpp / python: tree-sitter または antlr\n"
            + "csharp: tree-sitter または roslyn\n"
            + "default: sgconfig.yml の languageBackends に従い、指定がなければ tree-sitter。\n"
            + "パーサーによって kind 名（ノード種別名）が異なるため、kind を使うルールは対応するパーサーで実行してください。",
        CaseSensitiveTip = "オンにすると大文字と小文字を区別して照合します。",
        WholeWordTip = "単語の一部としての一致を除外します（例: \"log\" は \"logger\" に一致しない）。",
        RuleSourceTip =
            "sgconfig.yml: 設定ファイルの ruleDirs にあるルールをすべて実行\n"
            + "ルールファイル: 指定した 1 つの YAML ファイルを実行\n"
            + "インライン: パターン欄に書いた YAML を実行（複数ルールは --- で区切る）",
        RulePathTip =
            "sgconfig.yml またはルール YAML ファイルのパス。"
            + "sgconfig.yml を空欄にすると、検索対象から親フォルダへ向かって自動で探します。",
        RuleIdFilterTip = "ルールの id がこの正規表現に一致するルールだけを実行します。空欄ならすべて実行します。",
        PatternTip =
            "検索するパターン。AST モードではメタ変数を使えます。\n"
            + "$VAR: 任意の 1 ノードに一致（同じ名前は同じ内容）\n"
            + "$$$ARGS: 0 個以上のノードに一致\n"
            + "$_: 任意の 1 ノードに一致（キャプチャしない）\n"
            + "構文の詳細は「ヘルプ」を参照してください。",
        RewriteTip =
            "置換後のコード。パターンでキャプチャしたメタ変数（$VAR / $$$ARGS）を埋め込めます。\n"
            + "「置換プレビュー」で差分を確認してから適用します。空欄ならルールの fix を使います。",
        GlobsTip =
            "検索するファイルを glob で絞り込みます。複数は ; 区切り、先頭に ! を付けると除外。\n"
            + "パスは検索対象フォルダからの相対パスです（例: *.java; src/**; !**/*Test.java）。",
        ExcludeDirsTip = "検索しないディレクトリ名。複数は ; 区切り。どの階層にあっても除外します。",
        EncodingTip =
            "ファイルの文字コード。\n"
            + "auto: sgconfig.yml の encoding 指定に従い、指定がなければ BOM → UTF-8 → Shift_JIS → EUC-JP の順に判定\n"
            + "置換を適用するときも、元のファイルと同じ文字コードで書き戻します。",
        ContextLinesTip = "結果に表示する、マッチの前後の行数。",
        MaxResultsTip = "この件数に達した時点で検索を打ち切ります。",
        NoIgnoreTip = "オンにすると .gitignore の除外設定を無視し、除外対象のファイルも検索します。",
        IncludeHiddenTip = "オンにすると名前が . で始まるファイルやフォルダも検索します。",
        IncrementalTip = "オンにすると、入力が止まってから 0.5 秒後に自動で再検索します。",
    };

    public static readonly UiStrings En = new()
    {
        Code = "en",
        SwitchLanguage = "日本語",
        Root = "Search in",
        Browse = "Folder…",
        BrowseFile = "File…",
        Mode = "Mode",
        ModeAst = "AST pattern",
        ModeRule = "YAML rule",
        ModeText = "Text",
        ModeRegex = "Regex",
        Language = "Language",
        Strictness = "Strictness",
        Backend = "Parser",
        CaseSensitive = "Case sensitive",
        WholeWord = "Whole word",
        RuleSource = "Rules",
        RuleSourceConfig = "sgconfig.yml",
        RuleSourceFile = "Rule file",
        RuleSourceInline = "Inline",
        RulePath = "Path",
        RulePathConfigHint = "Empty: auto-detect sgconfig.yml upwards from the search root",
        RulePathFileHint = "Rule YAML file",
        RuleIdFilter = "rule-id filter (regex)",
        Pattern = "Pattern",
        PatternHintAst = "e.g. foo($$$ARGS)   metavariables: $VAR / $$$ARGS / $_",
        PatternHintRule = "Inline: rule YAML (separate multiple rules with ---)",
        PatternHintText = "Text to find",
        PatternHintRegex = "Regular expression (.NET syntax)",
        History = "History",
        NoHistory = "(no history)",
        Presets = "Presets",
        Help = "Help",
        Rewrite = "Rewrite",
        RewriteHint = "Rewrite template (e.g. bar($$$ARGS)). Empty: use the rule's fix",
        Advanced = "Advanced options",
        Globs = "Globs",
        GlobsHint = "e.g. *.java; src/**   (prefix ! to exclude)",
        ExcludeDirs = "Skip dirs",
        ExcludeDirsHint = "e.g. build; dist",
        Encoding = "Encoding",
        ContextLines = "Context lines",
        MaxResults = "Max hits",
        NoIgnore = "Ignore .gitignore",
        IncludeHidden = "Include hidden files",
        Incremental = "Incremental search",
        Search = "Search (F5)",
        Cancel = "Stop (Esc)",
        RewritePreview = "Rewrite preview…",
        Export = "Export…",
        Copy = "Copy results",
        Files = "Files",
        AllFiles = "(all)",
        CodeView = "Code",
        TableView = "Table",
        ColFile = "File",
        ColLine = "Line",
        ColColumn = "Col",
        ColText = "Match",
        ColMeta = "Metavariables",
        ColRule = "Rule",
        ColMessage = "Message",
        Find = "Find",
        FindNext = "Next",
        FindPrev = "Prev",
        OpenInApp = "Open in default app",
        CopyPath = "Copy path",
        Preview = "Preview",
        StatusReady = "Ready",
        StatusSearching = "Searching…",
        StatusDone = "{0} hits / {1} files ({2:F2} s)",
        StatusTruncated = " (stopped at max hits)",
        StatusCanceled = "Stopped",
        StatusError = "Error: ",
        Warnings = "{0} warnings",
        CodeViewTruncated = "… showing the first {0} hits only (see Table / Export for all)",
        ExportDone = "Exported: ",
        CopyDone = "Copied to clipboard",
        SettingsLoadFailed = "Could not load settings; started with defaults: ",
        RewriteTitle = "Rewrite preview",
        RewriteNothing = "Nothing to rewrite (enter a template, or search with rules that have a fix)",
        RewriteApplied = "Applied to {0} files",
        RewriteFailed = "Failed for {0} files",
        RewriteAstOnly = "Rewrite works only on AST / YAML rule results",
        Apply = "Apply to selected files",
        Close = "Close",
        SelectAll = "Select all",
        SelectNone = "Select none",
        PreviewTitle = "Preview",
        PrevHit = "◀ Previous hit",
        NextHit = "Next hit ▶",
        HelpTitle = "Pattern syntax help",
        HelpPattern = "Pattern syntax",
        HelpRule = "Rule YAML",
        RootTip = "Folder or file to search. Folders are searched recursively.",
        ModeTip =
            "AST pattern: compare code as syntax trees, ignoring formatting and whitespace\n"
            + "YAML rule: run ast-grep style rules (pattern / kind / has / inside, ...)\n"
            + "Text: plain substring match\n"
            + "Regex: .NET regular expression (^ and $ match at line boundaries)",
        LanguageTip =
            "Language used to parse the pattern.\n"
            + "auto: for each file extension, picks a language that can parse the pattern. "
            + "Languages that cannot parse it are listed as warnings.",
        StrictnessTip =
            "How strictly the pattern is compared with the code (examples in Java).\n\n"
            + "cst: exact match including punctuation and comments; extra nodes in the code fail the match\n"
            + "  e.g. foo($A, $B) does not match foo(1, /* c */ 2) (extra comment)\n"
            + "  e.g. if ($C) { $$$B } does not match an if with an else\n\n"
            + "smart (default): everything in the pattern must match; comments, modifiers (final, ...) "
            + "and trailing parts (else, ...) that only the code has are ignored\n"
            + "  e.g. foo($A, $B) matches foo(1, /* c */ 2)\n"
            + "  e.g. if ($C) { $$$B } also matches if (x) { … } else { … }\n"
            + "  e.g. $A + $B does not match 1 - 2 (different operator)\n\n"
            + "ast: compares named nodes only, ignoring operators, punctuation and comments. "
            + "Trailing nodes in the code are ignored too, so it matches broadly\n"
            + "  e.g. $A + $B also matches 1 - 2 and a < b\n"
            + "  e.g. bar($X) also matches bar(1, 2)\n\n"
            + "relaxed: ast with looser node-kind checks; usually gives the same results as ast",
        BackendTip =
            "Parser used to build syntax trees. Only these languages can switch; others are unaffected.\n"
            + "java / cpp / python: tree-sitter or antlr\n"
            + "csharp: tree-sitter or roslyn\n"
            + "default: languageBackends in sgconfig.yml, otherwise tree-sitter.\n"
            + "Kind names differ between parsers, so run kind-based rules with the parser they were written for.",
        CaseSensitiveTip = "When on, upper and lower case must match.",
        WholeWordTip = "Skip matches inside longer words (e.g. \"log\" does not match \"logger\").",
        RuleSourceTip =
            "sgconfig.yml: run every rule in the config's ruleDirs\n"
            + "Rule file: run one YAML file\n"
            + "Inline: run the YAML typed in the pattern box (separate rules with ---)",
        RulePathTip =
            "Path to sgconfig.yml or a rule YAML file. "
            + "Leave sgconfig.yml empty to search upwards from the search root.",
        RuleIdFilterTip = "Run only rules whose id matches this regular expression. Empty runs all rules.",
        PatternTip =
            "Pattern to search for. AST mode supports metavariables:\n"
            + "$VAR: any single node (the same name must match the same code)\n"
            + "$$$ARGS: zero or more nodes\n"
            + "$_: any single node, not captured\n"
            + "See Help for the full syntax.",
        RewriteTip =
            "Replacement code. Metavariables captured by the pattern ($VAR / $$$ARGS) can be used.\n"
            + "Check the diff in Rewrite preview before applying. Empty uses the rule's fix.",
        GlobsTip =
            "Filter files by glob. Separate with ;, prefix ! to exclude.\n"
            + "Paths are relative to the search folder (e.g. *.java; src/**; !**/*Test.java).",
        ExcludeDirsTip = "Directory names to skip at any depth. Separate with ;.",
        EncodingTip =
            "File encoding.\n"
            + "auto: encoding in sgconfig.yml if set, otherwise detected in order BOM → UTF-8 → Shift_JIS → EUC-JP.\n"
            + "Rewrites are written back in the file's original encoding.",
        ContextLinesTip = "Number of lines shown before and after each match.",
        MaxResultsTip = "Stop searching once this many hits are found.",
        NoIgnoreTip = "When on, .gitignore is not applied and ignored files are searched too.",
        IncludeHiddenTip = "When on, files and folders whose names start with . are searched too.",
        IncrementalTip = "When on, searches again 0.5 seconds after you stop typing.",
    };

    public static UiStrings For(string? code) =>
        (code ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) switch
        {
            "ja" => Ja,
            _ => En,
        };
}
