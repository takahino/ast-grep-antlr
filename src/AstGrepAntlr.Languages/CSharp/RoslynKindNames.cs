using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace AstGrepAntlr.Languages.CSharp;

/// <summary>Maps Roslyn <see cref="SyntaxKind"/> to snake_case names and tree-sitter-c-sharp aliases.</summary>
internal static class RoslynKindNames
{
    private static readonly Dictionary<SyntaxKind, string[]> AliasMap = BuildAliasMap();

    public static string ToSnakeCase(SyntaxKind kind) => PascalToSnake(kind.ToString());

    public static IReadOnlyList<string>? Aliases(SyntaxKind kind) =>
        AliasMap.TryGetValue(kind, out var aliases) ? aliases : null;

    public static IReadOnlyList<string> AllSnakeCaseNames()
    {
        var set = new HashSet<string>(StringComparer.Ordinal) { "modifier", "ERROR", "MISSING" };
        foreach (SyntaxKind kind in Enum.GetValues<SyntaxKind>())
        {
            set.Add(ToSnakeCase(kind));
            if (AliasMap.TryGetValue(kind, out var aliases))
            {
                foreach (var a in aliases)
                {
                    set.Add(a);
                }
            }
        }

        set.Add("integer_literal");
        set.Add("real_literal");
        return set.Order(StringComparer.Ordinal).ToList();
    }

    public static bool IsModifierKeyword(SyntaxKind kind) =>
        kind
            is SyntaxKind.PublicKeyword
                or SyntaxKind.PrivateKeyword
                or SyntaxKind.ProtectedKeyword
                or SyntaxKind.InternalKeyword
                or SyntaxKind.StaticKeyword
                or SyntaxKind.ReadOnlyKeyword
                or SyntaxKind.VirtualKeyword
                or SyntaxKind.OverrideKeyword
                or SyntaxKind.AbstractKeyword
                or SyntaxKind.SealedKeyword
                or SyntaxKind.PartialKeyword
                or SyntaxKind.AsyncKeyword
                or SyntaxKind.ExternKeyword
                or SyntaxKind.UnsafeKeyword
                or SyntaxKind.VolatileKeyword
                or SyntaxKind.RequiredKeyword
                or SyntaxKind.FileKeyword
                or SyntaxKind.NewKeyword
                or SyntaxKind.RefKeyword
                or SyntaxKind.ReadOnlyKeyword
                or SyntaxKind.ConstKeyword;

    public static bool IsNamedToken(SyntaxKind kind)
    {
        if (IsModifierKeyword(kind))
        {
            return true;
        }

        return kind
            is SyntaxKind.IdentifierToken
                or SyntaxKind.NumericLiteralToken
                or SyntaxKind.CharacterLiteralToken
                or SyntaxKind.StringLiteralToken
                or SyntaxKind.InterpolatedStringTextToken
                or SyntaxKind.SingleLineRawStringLiteralToken
                or SyntaxKind.MultiLineRawStringLiteralToken
                or SyntaxKind.Utf8StringLiteralToken
                or SyntaxKind.Utf8SingleLineRawStringLiteralToken
                or SyntaxKind.Utf8MultiLineRawStringLiteralToken
                or SyntaxKind.TrueKeyword
                or SyntaxKind.FalseKeyword
                or SyntaxKind.NullKeyword
                or SyntaxKind.ThisKeyword
                or SyntaxKind.BaseKeyword
                or SyntaxKind.DefaultKeyword
                or SyntaxKind.VarKeyword
                or SyntaxKind.TypeOfKeyword
                or SyntaxKind.SizeOfKeyword
                or SyntaxKind.NameOfKeyword
                or SyntaxKind.NewKeyword
                or SyntaxKind.StackAllocKeyword
                or SyntaxKind.CheckedKeyword
                or SyntaxKind.UncheckedKeyword
                or SyntaxKind.DelegateKeyword
                or SyntaxKind.AwaitKeyword
                or SyntaxKind.ThrowKeyword
                or SyntaxKind.ArgListKeyword;
    }

    private static Dictionary<SyntaxKind, string[]> BuildAliasMap()
    {
        var map = new Dictionary<SyntaxKind, string[]>();

        void Alias(SyntaxKind kind, params string[] aliases) => map[kind] = aliases;

        Alias(SyntaxKind.IdentifierName, "identifier");
        Alias(SyntaxKind.SimpleMemberAccessExpression, "member_access_expression");
        Alias(SyntaxKind.SimpleLambdaExpression, "lambda_expression");
        Alias(SyntaxKind.ParenthesizedLambdaExpression, "lambda_expression");
        Alias(SyntaxKind.StringLiteralExpression, "string_literal");
        Alias(SyntaxKind.CharacterLiteralExpression, "character_literal");
        Alias(SyntaxKind.TrueLiteralExpression, "boolean_literal");
        Alias(SyntaxKind.FalseLiteralExpression, "boolean_literal");
        Alias(SyntaxKind.NullLiteralExpression, "null_literal");
        Alias(SyntaxKind.NumericLiteralExpression, "integer_literal"); // overridden for reals in Aliases()

        // Binary operators → binary_expression
        foreach (
            var k in new[]
            {
                SyntaxKind.AddExpression,
                SyntaxKind.SubtractExpression,
                SyntaxKind.MultiplyExpression,
                SyntaxKind.DivideExpression,
                SyntaxKind.ModuloExpression,
                SyntaxKind.LeftShiftExpression,
                SyntaxKind.RightShiftExpression,
                SyntaxKind.UnsignedRightShiftExpression,
                SyntaxKind.LogicalOrExpression,
                SyntaxKind.LogicalAndExpression,
                SyntaxKind.BitwiseOrExpression,
                SyntaxKind.BitwiseAndExpression,
                SyntaxKind.ExclusiveOrExpression,
                SyntaxKind.EqualsExpression,
                SyntaxKind.NotEqualsExpression,
                SyntaxKind.LessThanExpression,
                SyntaxKind.LessThanOrEqualExpression,
                SyntaxKind.GreaterThanExpression,
                SyntaxKind.GreaterThanOrEqualExpression,
                SyntaxKind.IsExpression,
                SyntaxKind.AsExpression,
                SyntaxKind.CoalesceExpression,
            }
        )
        {
            Alias(k, "binary_expression");
        }

        // Assignments → assignment_expression
        foreach (
            var k in new[]
            {
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxKind.AddAssignmentExpression,
                SyntaxKind.SubtractAssignmentExpression,
                SyntaxKind.MultiplyAssignmentExpression,
                SyntaxKind.DivideAssignmentExpression,
                SyntaxKind.ModuloAssignmentExpression,
                SyntaxKind.AndAssignmentExpression,
                SyntaxKind.ExclusiveOrAssignmentExpression,
                SyntaxKind.OrAssignmentExpression,
                SyntaxKind.LeftShiftAssignmentExpression,
                SyntaxKind.RightShiftAssignmentExpression,
                SyntaxKind.UnsignedRightShiftAssignmentExpression,
                SyntaxKind.CoalesceAssignmentExpression,
            }
        )
        {
            Alias(k, "assignment_expression");
        }

        // Unary
        foreach (
            var k in new[]
            {
                SyntaxKind.UnaryPlusExpression,
                SyntaxKind.UnaryMinusExpression,
                SyntaxKind.BitwiseNotExpression,
                SyntaxKind.LogicalNotExpression,
                SyntaxKind.PreIncrementExpression,
                SyntaxKind.PreDecrementExpression,
                SyntaxKind.AddressOfExpression,
                SyntaxKind.PointerIndirectionExpression,
                SyntaxKind.IndexExpression,
            }
        )
        {
            Alias(k, "prefix_unary_expression");
        }

        Alias(SyntaxKind.PostIncrementExpression, "postfix_unary_expression");
        Alias(SyntaxKind.PostDecrementExpression, "postfix_unary_expression");
        Alias(SyntaxKind.SuppressNullableWarningExpression, "postfix_unary_expression");

        return map;
    }

    internal static bool LooksLikeRealLiteral(string text)
    {
        foreach (var c in text)
        {
            if (c is '.' or 'e' or 'E' or 'f' or 'F' or 'd' or 'D' or 'm' or 'M')
            {
                return true;
            }
        }

        return false;
    }

    private static string PascalToSnake(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                var prev = name[i - 1];
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (!char.IsUpper(prev) || nextIsLower)
                {
                    sb.Append('_');
                }
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
