using System.Text;

namespace AstGrepAntlr.Text;

/// <summary>
/// Maps between ANTLR code-point offsets, UTF-16 char offsets, and on-disk byte offsets.
/// </summary>
public sealed class OffsetMap
{
    private readonly int[] _codePointToUtf16;
    private readonly int[] _utf16ToByte;
    private readonly int _codePointCount;
    private readonly int _utf16Length;
    private readonly int _byteLength;
    private readonly int _bomLength;

    private OffsetMap(
        int[] codePointToUtf16,
        int[] utf16ToByte,
        int codePointCount,
        int utf16Length,
        int byteLength,
        int bomLength
    )
    {
        _codePointToUtf16 = codePointToUtf16;
        _utf16ToByte = utf16ToByte;
        _codePointCount = codePointCount;
        _utf16Length = utf16Length;
        _byteLength = byteLength;
        _bomLength = bomLength;
    }

    public int BomLength => _bomLength;
    public int Utf16Length => _utf16Length;
    public int ByteLength => _byteLength;
    public bool HasSurrogates => _codePointCount != _utf16Length;

    public static OffsetMap ForSource(string text, DetectedEncoding detected)
    {
        var hasSurrogates = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsSurrogate(text[i]))
            {
                hasSurrogates = true;
                break;
            }
        }

        int[] cpToUtf16;
        var cpCount = 0;
        if (!hasSurrogates)
        {
            cpToUtf16 = [];
            cpCount = text.Length;
        }
        else
        {
            var map = new List<int>(text.Length + 1);
            for (var i = 0; i < text.Length; )
            {
                map.Add(i);
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i += 2;
                }
                else
                {
                    i++;
                }
            }

            map.Add(text.Length);
            cpToUtf16 = map.ToArray();
            cpCount = cpToUtf16.Length - 1;
        }

        var bom = detected.HasBom ? detected.BomLength : 0;
        var enc = detected.Encoding;
        // Build UTF-16 → byte map for the decoded text (excluding BOM).
        var utf16ToByte = new int[text.Length + 1];
        utf16ToByte[0] = bom;
        var encoder = enc.GetEncoder();
        // Use a conservative approach: encode prefixes. For large files this is O(n²);
        // optimize for BMP-only UTF-8 (common case) with a linear scan.
        if (!hasSurrogates && enc.CodePage == 65001)
        {
            var bytePos = bom;
            for (var i = 0; i < text.Length; i++)
            {
                utf16ToByte[i] = bytePos;
                var ch = text[i];
                bytePos +=
                    ch <= 0x7F ? 1
                    : ch <= 0x7FF ? 2
                    : 3;
            }

            utf16ToByte[text.Length] = bytePos;
        }
        else if (!hasSurrogates && (enc.CodePage is 1200 or 1201))
        {
            for (var i = 0; i <= text.Length; i++)
            {
                utf16ToByte[i] = bom + i * 2;
            }
        }
        else if (!hasSurrogates && (enc.CodePage is 12000 or 12001))
        {
            for (var i = 0; i <= text.Length; i++)
            {
                utf16ToByte[i] = bom + i * 4;
            }
        }
        else
        {
            // General path: encode character by character (handles SJIS / surrogates).
            var bytePos = bom;
            var buf = new byte[8];
            for (var i = 0; i < text.Length; )
            {
                utf16ToByte[i] = bytePos;
                int len;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    len = enc.GetBytes(text, i, 2, buf, 0);
                    utf16ToByte[i + 1] = bytePos; // low surrogate shares start; end at i+2
                    i += 2;
                }
                else
                {
                    len = enc.GetBytes(text, i, 1, buf, 0);
                    i += 1;
                }

                bytePos += len;
            }

            utf16ToByte[text.Length] = bytePos;
        }

        return new OffsetMap(cpToUtf16, utf16ToByte, cpCount, text.Length, utf16ToByte[text.Length], bom);
    }

    public int CodePointToUtf16(int codePointOffset)
    {
        if (!HasSurrogates)
        {
            return Math.Clamp(codePointOffset, 0, _utf16Length);
        }

        if (codePointOffset <= 0)
        {
            return 0;
        }

        if (codePointOffset >= _codePointCount)
        {
            return _utf16Length;
        }

        return _codePointToUtf16[codePointOffset];
    }

    public int Utf16ToByte(int utf16Offset)
    {
        utf16Offset = Math.Clamp(utf16Offset, 0, _utf16Length);
        return _utf16ToByte[utf16Offset];
    }

    public (int ByteStart, int ByteEnd) Utf16RangeToBytes(int start, int end) => (Utf16ToByte(start), Utf16ToByte(end));
}
