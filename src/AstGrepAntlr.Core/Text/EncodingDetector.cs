using System.Security.Cryptography;
using System.Text;

namespace AstGrepAntlr.Text;

/// <summary>Detected encoding and BOM metadata for a source file.</summary>
public sealed record DetectedEncoding(Encoding Encoding, string Name, bool HasBom, int BomLength);

/// <summary>
/// Detects file encodings: explicit name, BOM, strict UTF-8, then scored Shift_JIS / EUC-JP.
/// </summary>
public static class EncodingDetector
{
    static EncodingDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static DetectedEncoding FromName(string name)
    {
        var encoding = ResolveNamed(name);
        var emitBom =
            name.Contains("bom", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "utf-16", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "utf-32", StringComparison.OrdinalIgnoreCase);
        return new DetectedEncoding(
            WithFallbacks(encoding, emitBom),
            NormalizeName(encoding, emitBom),
            emitBom,
            BomLengthOf(encoding, emitBom)
        );
    }

    public static DetectedEncoding Detect(byte[] bytes, string? explicitName = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return FromName(explicitName);
        }

        if (TryBom(bytes, out var bom))
        {
            return bom;
        }

        if (TryStrictUtf8(bytes, out var utf8))
        {
            return utf8;
        }

        if (TryLegacy(bytes, out var legacy))
        {
            return legacy;
        }

        throw new DecoderFallbackException("Unable to decode file as UTF-8, Shift_JIS (cp932), or EUC-JP.");
    }

    public static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static byte[] Encode(string text, DetectedEncoding detected)
    {
        var enc = WithFallbacks(detected.Encoding, detected.HasBom);
        var body = enc.GetBytes(text);
        if (!detected.HasBom)
        {
            return body;
        }

        var preamble = enc.GetPreamble();
        if (preamble.Length == 0)
        {
            return body;
        }

        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    public static string Decode(byte[] bytes, DetectedEncoding detected)
    {
        var enc = WithFallbacks(detected.Encoding, emitBom: false);
        var start = detected.HasBom ? detected.BomLength : 0;
        if (start > bytes.Length)
        {
            start = 0;
        }

        return enc.GetString(bytes, start, bytes.Length - start);
    }

    private static bool TryBom(byte[] bytes, out DetectedEncoding detected)
    {
        detected = null!;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            detected = new DetectedEncoding(new UTF8Encoding(true, true), "utf-8-bom", true, 3);
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            if (bytes.Length >= 4 && bytes[2] == 0x00 && bytes[3] == 0x00)
            {
                detected = new DetectedEncoding(new UTF32Encoding(false, true, true), "utf-32le-bom", true, 4);
                return true;
            }

            detected = new DetectedEncoding(new UnicodeEncoding(false, true, true), "utf-16le-bom", true, 2);
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            detected = new DetectedEncoding(new UnicodeEncoding(true, true, true), "utf-16be-bom", true, 2);
            return true;
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            detected = new DetectedEncoding(new UTF32Encoding(true, true, true), "utf-32be-bom", true, 4);
            return true;
        }

        return false;
    }

    private static bool TryStrictUtf8(byte[] bytes, out DetectedEncoding detected)
    {
        detected = null!;
        try
        {
            var enc = new UTF8Encoding(false, true);
            _ = enc.GetString(bytes);
            detected = new DetectedEncoding(enc, "utf-8", false, 0);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool TryLegacy(byte[] bytes, out DetectedEncoding detected)
    {
        detected = null!;
        var candidates = new (string Name, Encoding Enc)[]
        {
            (
                "shift_jis",
                Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            ),
            (
                "euc-jp",
                Encoding.GetEncoding(51932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            ),
        };

        DetectedEncoding? best = null;
        var bestScore = double.MinValue;
        foreach (var (name, enc) in candidates)
        {
            try
            {
                var text = enc.GetString(bytes);
                var score = ScoreJapanese(text);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new DetectedEncoding(enc, name, false, 0);
                }
            }
            catch (DecoderFallbackException)
            {
                // skip
            }
        }

        if (best is null || bestScore < 0)
        {
            return false;
        }

        detected = best;
        return true;
    }

    private static double ScoreJapanese(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var kana = 0;
        var kanji = 0;
        var ascii = 0;
        var weird = 0;
        foreach (var ch in text)
        {
            if (ch is >= '\u3040' and <= '\u30FF')
            {
                kana++;
            }
            else if (ch is >= '\u4E00' and <= '\u9FFF')
            {
                kanji++;
            }
            else if (ch < 0x80)
            {
                ascii++;
            }
            else if (char.IsControl(ch) && ch is not '\t' and not '\n' and not '\r')
            {
                weird++;
            }
        }

        return kana * 3.0 + kanji * 2.0 + ascii * 0.1 - weird * 10.0;
    }

    private static Encoding ResolveNamed(string name)
    {
        var n = name.Trim().ToLowerInvariant().Replace('_', '-');
        if (n.EndsWith("-bom", StringComparison.Ordinal))
        {
            n = n[..^4];
        }

        return n switch
        {
            "utf-8" or "utf8" => new UTF8Encoding(false, true),
            "utf-16" or "utf-16le" or "utf16" or "unicode" => new UnicodeEncoding(false, true, true),
            "utf-16be" => new UnicodeEncoding(true, true, true),
            "utf-32" or "utf-32le" => new UTF32Encoding(false, true, true),
            "utf-32be" => new UTF32Encoding(true, true, true),
            "shift-jis" or "shiftjis" or "sjis" or "cp932" or "windows-31j" => Encoding.GetEncoding(
                932,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback
            ),
            "euc-jp" or "eucjp" => Encoding.GetEncoding(
                51932,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback
            ),
            _ => Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        };
    }

    private static Encoding WithFallbacks(Encoding encoding, bool emitBom) =>
        encoding.CodePage switch
        {
            65001 => new UTF8Encoding(emitBom, true),
            1200 => new UnicodeEncoding(false, emitBom, true),
            1201 => new UnicodeEncoding(true, emitBom, true),
            12000 => new UTF32Encoding(false, emitBom, true),
            12001 => new UTF32Encoding(true, emitBom, true),
            932 => Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            51932 => Encoding.GetEncoding(51932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            _ => encoding,
        };

    private static int BomLengthOf(Encoding encoding, bool hasBom)
    {
        if (!hasBom)
        {
            return 0;
        }

        return encoding.CodePage switch
        {
            65001 => 3,
            1200 or 1201 => 2,
            12000 or 12001 => 4,
            _ => encoding.GetPreamble().Length,
        };
    }

    private static string NormalizeName(Encoding encoding, bool hasBom) =>
        encoding.CodePage switch
        {
            65001 => hasBom ? "utf-8-bom" : "utf-8",
            1200 => "utf-16le-bom",
            1201 => "utf-16be-bom",
            12000 => "utf-32le-bom",
            12001 => "utf-32be-bom",
            932 => "shift_jis",
            51932 => "euc-jp",
            _ => encoding.WebName,
        };
}
