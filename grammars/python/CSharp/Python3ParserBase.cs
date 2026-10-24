// Vendored from grammars-v4 python/python3/CSharp/Python3ParserBase.cs (MIT).
// Namespace adjusted to match Antlr4BuildTasks Package setting.
using System.IO;
using Antlr4.Runtime;

namespace AstGrepAntlr.Languages.Python;

public abstract class Python3ParserBase : Parser
{
    protected Python3ParserBase(ITokenStream input)
        : base(input) { }

    protected Python3ParserBase(ITokenStream input, TextWriter output, TextWriter errorOutput)
        : base(input, output, errorOutput) { }

    public bool CannotBePlusMinus() => true;

    public bool CannotBeDotLpEq() => true;
}
