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
}
