using System.Text;
using AstGrepAntlr.Tree;

namespace AstGrepAntlr.Output;

/// <summary>Renders a syntax tree as indented text, shared by `ag4 debug` and the MCP dump tool.</summary>
public static class TreeDumper
{
    public static string ToText(SyntaxTree tree, int root)
    {
        var sb = new StringBuilder();
        Append(sb, tree, root, depth: 0);
        return sb.ToString();
    }

    public static void Write(SyntaxTree tree, int root, TextWriter writer) => writer.Write(ToText(tree, root));

    private static void Append(StringBuilder sb, SyntaxTree tree, int index, int depth)
    {
        if (index < 0)
        {
            return;
        }

        var n = tree[index];
        sb.Append(' ', depth * 2).Append(n.TypeName).Append(" [").Append(n.Start).Append(',').Append(n.End).Append(']');

        if (!n.IsNamed)
        {
            sb.Append(" unnamed");
        }

        if (n.MetaVarName is not null)
        {
            sb.Append(" metavar=$").Append(n.IsMultiMetaVar ? "$$" : string.Empty).Append(n.MetaVarName);
        }

        if (n.IsTerminal)
        {
            sb.Append(" '").Append(tree.Text(index).Replace("\n", "\\n")).Append('\'');
        }

        sb.AppendLine();
        foreach (var c in tree.Children(index))
        {
            Append(sb, tree, c, depth + 1);
        }
    }
}
