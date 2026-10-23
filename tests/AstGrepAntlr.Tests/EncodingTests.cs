using System.Text;
using AstGrepAntlr.Languages;
using AstGrepAntlr.Rewrite;
using AstGrepAntlr.Search;
using AstGrepAntlr.Tests.Harness;
using AstGrepAntlr.Text;
using Xunit;

namespace AstGrepAntlr.Tests;

public class EncodingTests
{
    static EncodingTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static IEnumerable<object[]> RoundTripMatrix()
    {
        var encodings = new[]
        {
            "utf-8",
            "utf-8-bom",
            "utf-16le-bom",
            "utf-16be-bom",
            "utf-32le-bom",
            "shift_jis",
            "euc-jp",
        };
        var newlines = new[] { "lf", "crlf" };
        var contents = new[] { "ascii", "kana", "kanji" };
        var edits = new[] { "middle", "end" };
        return Matrix.Product(encodings, newlines, contents, edits);
    }

    [Theory]
    [MemberData(nameof(RoundTripMatrix))]
    public void Round_trip_preserves_encoding_and_content(
        string id,
        string encName,
        string newline,
        string content,
        string edit
    )
    {
        _ = id;
        _ = edit;
        using var dir = new TempDir("ag4-enc");
        var text = BuildSource(content, newline);
        var detected = EncodingDetector.FromName(encName);
        byte[] originalBytes;
        try
        {
            originalBytes = EncodingDetector.Encode(text, detected);
        }
        catch (EncoderFallbackException)
        {
            return;
        }

        var path = dir.WriteBytes("Sample.java", originalBytes);

        // Force the same encoding for search/apply so ASCII-only SJIS is not re-detected as UTF-8.
        var registry = BuiltinLanguages.CreateRegistry();
        var matches = new Searcher()
            .RunPattern(
                "System.out.println($A)",
                new SearchOptions
                {
                    Registry = registry,
                    LanguageId = "java",
                    Paths = [path],
                    Encoding = encName,
                }
            )
            .ToList();
        Assert.NotEmpty(matches);

        Rewriter.RewriteFile(path, matches, _ => "System.out.println(9)", apply: true, explicitEncoding: encName);

        var after = SourceFile.Read(path, encName);
        Assert.Equal(detected.Name, after.Detected.Name);
        Assert.Contains("println(9)", after.Text, StringComparison.Ordinal);
        if (content is "kana" or "kanji")
        {
            Assert.Contains(content == "kana" ? "あ" : "漢字", after.Text, StringComparison.Ordinal);
        }

        if (detected.HasBom)
        {
            var preamble = EncodingDetector.FromName(encName).Encoding.GetPreamble();
            Assert.True(after.Bytes.AsSpan(0, preamble.Length).SequenceEqual(preamble));
        }
    }

    [Fact]
    public void Shift_jis_auto_detect_round_trip()
    {
        using var dir = new TempDir("ag4-sjis");
        var text = "class Sample { void m() { System.out.println(1); /* 日本語 */ } }\n";
        var detected = EncodingDetector.FromName("shift_jis");
        var path = dir.WriteBytes("Sample.java", EncodingDetector.Encode(text, detected));

        var registry = BuiltinLanguages.CreateRegistry();
        var matches = new Searcher()
            .RunPattern(
                "System.out.println($A)",
                new SearchOptions
                {
                    Registry = registry,
                    LanguageId = "java",
                    Paths = [path],
                }
            )
            .ToList();
        Assert.NotEmpty(matches);
        Assert.Equal("shift_jis", matches[0].Encoding);

        Rewriter.RewriteFile(path, matches, _ => "System.out.println(9)", apply: true);

        var after = SourceFile.Read(path);
        Assert.Equal("shift_jis", after.EncodingName);
        Assert.Contains("日本語", after.Text, StringComparison.Ordinal);
        Assert.Contains("println(9)", after.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_aborts_on_unencodable_replacement()
    {
        using var dir = new TempDir("ag4-enc-fail");
        var detected = EncodingDetector.FromName("shift_jis");
        var text = "class Sample { void m() { System.out.println(1); } }\n";
        var path = dir.WriteBytes("Sample.java", EncodingDetector.Encode(text, detected));
        var source = SourceFile.Read(path, "shift_jis");
        var bad = source.Text.Replace("1", "\u2603", StringComparison.Ordinal);
        Assert.Throws<EncoderFallbackException>(() => EncodingDetector.Encode(bad, source.Detected));
        Assert.Throws<InvalidOperationException>(() => Rewriter.ApplyAtomic(path, bad, source.Hash, source.Detected));
        Assert.Equal(source.Bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Apply_aborts_on_hash_mismatch()
    {
        using var dir = new TempDir("ag4-hash");
        var path = dir.WriteText("A.java", "class A {}\n");
        var source = SourceFile.Read(path);
        File.WriteAllText(path, "class B {}\n");
        Assert.Throws<InvalidOperationException>(() =>
            Rewriter.ApplyAtomic(path, "class C {}\n", source.Hash, source.Detected)
        );
    }

    [Fact]
    public void Explicit_encoding_overrides_detection()
    {
        using var dir = new TempDir("ag4-force");
        var path = dir.WriteText("A.java", "class A { void m(){ System.out.println(1); } }\n");
        var source = SourceFile.Read(path, "shift_jis");
        Assert.Equal("shift_jis", source.EncodingName);
    }

    private static string BuildSource(string content, string newline)
    {
        var comment = content switch
        {
            "kana" => " /* あいう */ ",
            "kanji" => " /* 漢字テスト */ ",
            _ => " /* ascii */ ",
        };
        var nl = newline == "crlf" ? "\r\n" : "\n";
        return $"class Sample {{ void m() {{{comment}System.out.println(1); }} }}{nl}";
    }
}
