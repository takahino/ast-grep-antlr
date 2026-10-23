// Vendored from grammars-v4 cpp/CSharp/CPP14ParserBase.cs (MIT).
// Namespace adjusted to match Antlr4BuildTasks Package setting.
using Antlr4.Runtime;
using System.IO;

namespace AstGrepAntlr.Languages.Cpp;

public abstract class CPP14ParserBase : Parser
{
    protected CPP14ParserBase(ITokenStream input, TextWriter output, TextWriter errorOutput)
        : base(input, output, errorOutput) { }

    public bool IsPureSpecifierAllowed()
    {
        try
        {
            var x = Context;
            var c = x.GetChild(0).GetChild(0);
            var c2 = c.GetChild(0);
            var p = c2.GetChild(1);
            if (p == null)
            {
                return false;
            }

            return p.GetType() == typeof(CPP14Parser.ParametersAndQualifiersContext);
        }
        catch
        {
            // ignored
        }

        return false;
    }
}
