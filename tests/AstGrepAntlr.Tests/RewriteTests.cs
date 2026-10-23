using AstGrepAntlr.Rewrite;
using AstGrepAntlr.Tests.Harness;
using Xunit;

namespace AstGrepAntlr.Tests;

public class RewriteTests
{
    [Theory]
    [InlineData("$$$ARGS", "1, 2", "1, 2")]
    [InlineData("$A", "x", "x")]
    [InlineData("before $$$ARGS after", "1", "before 1 after")]
    [InlineData("$A$B", "ab", "ab")] // A="", B won't — uses meta map below
    public void ApplyTemplate_prefers_triple_dollar_names(string template, string unused1, string unused2)
    {
        _ = unused1;
        _ = unused2;
        var meta = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ARGS"] = "1, 2",
            ["A"] = "a",
            ["B"] = "b",
            ["AB"] = "ab",
        };
        if (template == "$$$ARGS")
        {
            Assert.Equal("1, 2", Rewriter.ApplyTemplate(template, meta, "FALLBACK"));
        }
        else if (template == "$A")
        {
            Assert.Equal("a", Rewriter.ApplyTemplate(template, meta, "FALLBACK"));
        }
        else if (template.Contains("$$$ARGS", StringComparison.Ordinal))
        {
            Assert.Equal("before 1, 2 after", Rewriter.ApplyTemplate(template, meta, "FALLBACK"));
        }
        else if (template == "$A$B")
        {
            Assert.Equal("ab", Rewriter.ApplyTemplate(template, meta, "FALLBACK"));
        }
    }

    [Fact]
    public void ApplyTemplate_bare_triple_uses_matched_text()
    {
        var meta = new Dictionary<string, string>(StringComparer.Ordinal) { ["A"] = "1" };
        Assert.Equal("WRAP(foo)", Rewriter.ApplyTemplate("WRAP($$$)", meta, "foo"));
    }

    [Fact]
    public void ApplyTemplate_does_not_eat_triple_name_via_single_dollar()
    {
        // Old bug: replacing $ARGS before $$$ARGS left "$$" + value.
        var meta = new Dictionary<string, string>(StringComparer.Ordinal) { ["ARGS"] = "x" };
        Assert.Equal("call(x)", Rewriter.ApplyTemplate("call($$$ARGS)", meta, "FALLBACK"));
    }

    [Fact]
    public void UnifiedDiff_has_context_hunks_and_round_trips()
    {
        var oldText = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"line{i}")) + "\n";
        var newText = oldText.Replace("line10", "line10-changed", StringComparison.Ordinal);
        var diff = UnifiedDiff.Make("f.txt", oldText, newText);
        Assert.Contains("@@", diff, StringComparison.Ordinal);
        Assert.Contains("-line10", diff, StringComparison.Ordinal);
        Assert.Contains("+line10-changed", diff, StringComparison.Ordinal);
        Assert.DoesNotContain("-line1\n", diff, StringComparison.Ordinal); // not a full-file dump
        var restored = PatchOracle.Apply(oldText, diff);
        Assert.Equal(newText.Replace("\r\n", "\n"), restored.Replace("\r\n", "\n"));
    }

    [Theory]
    [MemberData(nameof(RandomDiffCases))]
    [Trait("Category", "Variation")]
    public void UnifiedDiff_random_edits_round_trip(string id, int caseIndex)
    {
        var rng = new Random(TestSeed.Value + caseIndex);
        var lines = Enumerable.Range(0, 30).Select(i => $"L{i}-{rng.Next(1000)}").ToList();
        var oldText = string.Join('\n', lines) + "\n";
        var newLines = lines.ToList();
        for (var i = 0; i < 5; i++)
        {
            var op = rng.Next(3);
            var at = rng.Next(Math.Max(1, newLines.Count));
            if (op == 0 && newLines.Count > 0)
            {
                newLines.RemoveAt(at % newLines.Count);
            }
            else if (op == 1)
            {
                newLines.Insert(at % (newLines.Count + 1), $"N{i}-{rng.Next(1000)}");
            }
            else if (newLines.Count > 0)
            {
                newLines[at % newLines.Count] = $"M{i}-{rng.Next(1000)}";
            }
        }

        var newText = string.Join('\n', newLines) + "\n";
        var diff = UnifiedDiff.Make("r.txt", oldText, newText);
        if (oldText == newText)
        {
            Assert.Equal(string.Empty, diff);
            return;
        }

        var restored = PatchOracle.Apply(oldText, diff);
        Assert.True(
            string.Equals(newText.Replace("\r\n", "\n"), restored.Replace("\r\n", "\n"), StringComparison.Ordinal),
            TestSeed.FailMessage(caseIndex, $"id={id} restore mismatch")
        );
    }

    public static IEnumerable<object[]> RandomDiffCases()
    {
        for (var i = 0; i < 40; i++)
        {
            yield return [$"diff/{i}", i];
        }
    }
}
