namespace AstGrepAntlr.Languages.TreeSitter;

/// <summary>Factory for built-in tree-sitter language front-ends.</summary>
public static class TreeSitterBuiltins
{
    public static TreeSitterLanguage Java() =>
        new(
            id: "java",
            extensions: [".java"],
            libraryStem: "tree-sitter-java",
            exportName: "tree_sitter_java",
            startRule: "program",
            contexts:
            [
                // Statement before expression so `$VAR = $VAL;` / `break;` are not mis-accepted
                // as a bogus local_variable_declaration inside `Object __x = …`.
                new PatternContext("statement", "class __AG4 { void __m() { {0} } }"),
                new PatternContext("block", "class __AG4 { void __m() { {0} } }"),
                new PatternContext("local_variable_declaration", "class __AG4 { void __m() { {0} } }"),
                new PatternContext("expression", "class __AG4 { void __m() { Object __x = {0}; } }"),
                new PatternContext("class_body", "class __AG4 { {0} }"),
                new PatternContext("method_declaration", "class __AG4 { {0} }"),
                // After method/class_body so @Override / this() do not steal ordinary statements.
                new PatternContext("modifiers", "class __AG4 { {0} void __m() {} }"),
                new PatternContext("constructor_body", "class __AG4 { __AG4() { {0} } }"),
                new PatternContext("class_declaration", "{0}"),
                new PatternContext("program", "{0}"),
            ],
            metaVarVariants:
            [
                "{0}",
                "{0};",
                "",
                "int {0}",
                "final {0}",
                "{0} {0}",
                "{0} {0};",
                "int {0};",
                "void {0}(){}",
                ";",
                "int",
                "String",
            ]
        );

    public static TreeSitterLanguage Cpp() =>
        new(
            id: "cpp",
            extensions: [".cpp", ".cc", ".cxx", ".c++", ".hpp", ".hh", ".hxx", ".h"],
            libraryStem: "tree-sitter-cpp",
            exportName: "tree_sitter_cpp",
            startRule: "translation_unit",
            contexts:
            [
                new PatternContext("statement", "void __ag4() { {0} }"),
                new PatternContext("expression", "void __ag4() { auto __x = {0}; }"),
                new PatternContext("declaration", "{0}"),
                new PatternContext("function_definition", "{0}"),
                // Bare translation_unit first for #include; trailing \n in a wrap often breaks exact-span.
                new PatternContext("preprocessor", "{0}"),
                new PatternContext("translation_unit", "{0}"),
            ],
            metaVarVariants: ["{0}", "{0};", "", "int {0}", "{0} {0}", "int {0};", "void {0}(){}", ";"]
        );

    public static TreeSitterLanguage Python() =>
        new(
            id: "python",
            extensions: [".py"],
            libraryStem: "tree-sitter-python",
            exportName: "tree_sitter_python",
            startRule: "module",
            contexts:
            [
                new PatternContext("statement", "{0}\n"),
                new PatternContext("block", "def __ag4():\n    {0}\n"),
                new PatternContext("expression", "__x = {0}\n"),
                new PatternContext("primary_expression", "__x = {0}\n"),
                new PatternContext("function_definition", "{0}\n"),
                new PatternContext("class_definition", "{0}\n"),
                new PatternContext("module", "{0}\n"),
            ],
            metaVarVariants: ["{0}", "{0}\n", "", "pass", "x = {0}", "def {0}():\n    pass\n"]
        );

    public static TreeSitterLanguage CSharp() =>
        new(
            id: "csharp",
            extensions: [".cs"],
            libraryStem: "tree-sitter-c-sharp",
            exportName: "tree_sitter_c_sharp",
            startRule: "compilation_unit",
            contexts:
            [
                // Statement before expression so `$VAR = $VAL;` / `break;` are not mis-accepted
                // as a bogus local_declaration_statement inside `var __x = …`.
                new PatternContext("statement", "class __AG4 { void __m() { {0} } }"),
                new PatternContext("block", "class __AG4 { void __m() { {0} } }"),
                new PatternContext("local_declaration_statement", "class __AG4 { void __m() { {0} } }"),
                new PatternContext("expression", "class __AG4 { void __m() { var __x = {0}; } }"),
                new PatternContext("member_declaration", "class __AG4 { {0} }"),
                new PatternContext("class_declaration", "{0}"),
                // Last: bare compilation_unit so top-level statements are not preferred over wraps.
                new PatternContext("compilation_unit", "{0}"),
            ],
            metaVarVariants:
            [
                "{0}",
                "{0};",
                "",
                "int {0}",
                "{0} {0}",
                "{0} {0};",
                "int {0};",
                "void {0}(){}",
                ";",
                "int",
                "string",
            ]
        );

    public static TreeSitterLanguage JavaScript() => EcmaScript("javascript", [".js", ".mjs", ".cjs", ".jsx"]);

    public static TreeSitterLanguage TypeScript() => EcmaScript("typescript", [".ts", ".mts", ".cts"]);

    public static TreeSitterLanguage Tsx() => EcmaScript("tsx", [".tsx"]);

    /// <summary>JS / TS / TSX share statement and expression shapes; only the grammar differs.</summary>
    private static TreeSitterLanguage EcmaScript(string id, IReadOnlyList<string> extensions) =>
        new(
            id: id,
            extensions: extensions,
            libraryStem: $"tree-sitter-{id}",
            exportName: $"tree_sitter_{id}",
            startRule: "program",
            contexts:
            [
                new PatternContext("statement", "function __ag4() { {0} }"),
                new PatternContext("expression", "const __x = {0};"),
                new PatternContext("class_body", "class __AG4 { {0} }"),
                new PatternContext("program", "{0}"),
            ],
            metaVarVariants: ["{0}", "{0};", ""]
        );

    public static TreeSitterLanguage Go() =>
        new(
            id: "go",
            extensions: [".go"],
            libraryStem: "tree-sitter-go",
            exportName: "tree_sitter_go",
            startRule: "source_file",
            contexts:
            [
                new PatternContext("statement", "package __ag4\nfunc __ag4() { {0} }"),
                new PatternContext("expression", "package __ag4\nvar __x = {0}"),
                new PatternContext("declaration", "package __ag4\n{0}"),
                new PatternContext("source_file", "{0}"),
            ],
            metaVarVariants: ["{0}", ""]
        );

    public static TreeSitterLanguage Rust() =>
        new(
            id: "rust",
            extensions: [".rs"],
            libraryStem: "tree-sitter-rust",
            exportName: "tree_sitter_rust",
            startRule: "source_file",
            contexts:
            [
                new PatternContext("statement", "fn __ag4() { {0} }"),
                new PatternContext("expression", "fn __ag4() { let __x = {0}; }"),
                new PatternContext("declaration_list", "impl __AG4 { {0} }"),
                new PatternContext("source_file", "{0}"),
            ],
            metaVarVariants: ["{0}", "{0};", ""]
        );

    public static TreeSitterLanguage C() =>
        new(
            id: "c",
            extensions: [".c"],
            libraryStem: "tree-sitter-c",
            exportName: "tree_sitter_c",
            startRule: "translation_unit",
            contexts:
            [
                // Expression before statement: as a statement, `printf($$$A)` parses as a
                // macro_type_specifier instead of a call_expression.
                new PatternContext("expression", "void __ag4() { int __x = {0}; }"),
                new PatternContext("statement", "void __ag4() { {0} }"),
                new PatternContext("declaration", "{0}"),
                new PatternContext("function_definition", "{0}"),
                new PatternContext("preprocessor", "{0}"),
                new PatternContext("translation_unit", "{0}"),
            ],
            metaVarVariants: ["{0}", "{0};", "", "int {0}", "{0} {0}", "int {0};", ";"]
        );

    public static TreeSitterLanguage Ruby() =>
        new(
            id: "ruby",
            extensions: [".rb", ".rake", ".gemspec"],
            libraryStem: "tree-sitter-ruby",
            exportName: "tree_sitter_ruby",
            startRule: "program",
            contexts:
            [
                new PatternContext("statement", "{0}\n"),
                new PatternContext("expression", "__x = {0}\n"),
                new PatternContext("body_statement", "def __ag4\n{0}\nend\n"),
                new PatternContext("program", "{0}\n"),
            ],
            metaVarVariants: ["{0}", "", "{0}\n"]
        );

    public static TreeSitterLanguage Php() =>
        new(
            id: "php",
            extensions: [".php"],
            libraryStem: "tree-sitter-php",
            exportName: "tree_sitter_php",
            startRule: "program",
            contexts:
            [
                new PatternContext("statement", "<?php\n{0}"),
                new PatternContext("expression", "<?php\n$__x = {0};"),
                new PatternContext("declaration_list", "<?php\nclass __AG4 { {0} }"),
                new PatternContext("program", "{0}"),
            ],
            // `$A` → `$__MV0__` lets a metavariable stand in a PHP variable position (`$A = 1;`).
            metaVarVariants: ["{0}", "${0}", "{0};", ""]
        );

    public static TreeSitterLanguage Bash() =>
        new(
            id: "bash",
            extensions: [".sh", ".bash"],
            libraryStem: "tree-sitter-bash",
            exportName: "tree_sitter_bash",
            startRule: "program",
            contexts:
            [
                new PatternContext("statement", "{0}\n"),
                new PatternContext("function_body", "__ag4() {\n{0}\n}\n"),
                new PatternContext("program", "{0}"),
            ],
            metaVarVariants: ["{0}", ""]
        );

    public static TreeSitterLanguage Html() =>
        new(
            id: "html",
            extensions: [".html", ".htm"],
            libraryStem: "tree-sitter-html",
            exportName: "tree_sitter_html",
            startRule: "document",
            contexts: [new PatternContext("element", "<div>{0}</div>"), new PatternContext("document", "{0}")],
            metaVarVariants: ["{0}", ""]
        );

    public static TreeSitterLanguage Css() =>
        new(
            id: "css",
            extensions: [".css"],
            libraryStem: "tree-sitter-css",
            exportName: "tree_sitter_css",
            startRule: "stylesheet",
            contexts:
            [
                new PatternContext("block", "__ag4 { {0} }"),
                new PatternContext("value", "__ag4 { __x: {0}; }"),
                new PatternContext("stylesheet", "{0}"),
            ],
            metaVarVariants: ["{0}", ""]
        );

    public static TreeSitterLanguage Json() =>
        new(
            id: "json",
            extensions: [".json"],
            libraryStem: "tree-sitter-json",
            exportName: "tree_sitter_json",
            startRule: "document",
            contexts: [new PatternContext("document", "{0}"), new PatternContext("object", "{{0}}")],
            // A bare identifier is not JSON; a quoted placeholder stands in for any key or value.
            metaVarVariants: ["\"{0}\"", "{0}", ""]
        );

    public static TreeSitterLanguage Toml() =>
        new(
            id: "toml",
            extensions: [".toml"],
            libraryStem: "tree-sitter-toml",
            exportName: "tree_sitter_toml",
            startRule: "document",
            contexts: [new PatternContext("document", "{0}\n"), new PatternContext("value", "__x = {0}\n")],
            metaVarVariants: ["{0}", "\"{0}\"", ""]
        );

    public static TreeSitterLanguage Scala() =>
        new(
            id: "scala",
            extensions: [".scala", ".sc"],
            libraryStem: "tree-sitter-scala",
            exportName: "tree_sitter_scala",
            startRule: "compilation_unit",
            contexts:
            [
                new PatternContext("block", "object __AG4 { def __m = { {0} } }"),
                new PatternContext("expression", "object __AG4 { val __x = {0} }"),
                new PatternContext("template_body", "object __AG4 { {0} }"),
                new PatternContext("compilation_unit", "{0}"),
            ],
            metaVarVariants: ["{0}", ""]
        );

    public static TreeSitterLanguage Haskell() =>
        new(
            id: "haskell",
            extensions: [".hs"],
            libraryStem: "tree-sitter-haskell",
            exportName: "tree_sitter_haskell",
            startRule: "haskell",
            contexts:
            [
                new PatternContext("expression", "__x = {0}\n"),
                new PatternContext("declarations", "{0}\n"),
                new PatternContext("haskell", "{0}"),
            ],
            metaVarVariants: ["{0}", ""]
        );

    public static TreeSitterLanguage Julia() =>
        new(
            id: "julia",
            extensions: [".jl"],
            libraryStem: "tree-sitter-julia",
            exportName: "tree_sitter_julia",
            startRule: "source_file",
            contexts:
            [
                new PatternContext("statement", "{0}\n"),
                new PatternContext("expression", "__x = {0}\n"),
                new PatternContext("block", "function __ag4()\n{0}\nend\n"),
                new PatternContext("source_file", "{0}"),
            ],
            metaVarVariants: ["{0}", ""]
        );

    public static TreeSitterLanguage OCaml() =>
        new(
            id: "ocaml",
            extensions: [".ml"],
            libraryStem: "tree-sitter-ocaml",
            exportName: "tree_sitter_ocaml",
            startRule: "compilation_unit",
            contexts:
            [
                new PatternContext("expression", "let __x = {0}"),
                new PatternContext("compilation_unit", "{0}"),
            ],
            metaVarVariants: ["{0}", ""]
        );

    public static TreeSitterLanguage Verilog() =>
        new(
            id: "verilog",
            extensions: [".v", ".sv"],
            libraryStem: "tree-sitter-verilog",
            exportName: "tree_sitter_verilog",
            startRule: "source_file",
            contexts:
            [
                // Module items first so `assign $L = $R;` is a continuous_assign, not a procedural one.
                new PatternContext("module_item", "module __ag4;\n{0}\nendmodule\n"),
                new PatternContext("statement", "module __ag4;\ninitial begin\n{0}\nend\nendmodule\n"),
                new PatternContext("expression", "module __ag4;\nassign __x = {0};\nendmodule\n"),
                new PatternContext("source_file", "{0}"),
            ],
            metaVarVariants: ["{0}", "{0};", ""]
        );
}
