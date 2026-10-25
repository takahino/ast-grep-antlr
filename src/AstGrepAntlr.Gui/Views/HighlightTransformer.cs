using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace AstGrepAntlr.Gui.Views;

/// <summary>Paints match ranges (absolute document offsets) and optionally emphasizes whole header lines.</summary>
public sealed class HighlightTransformer : DocumentColorizingTransformer
{
    public static readonly IBrush MatchBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xC8, 0x2E));

    private readonly IReadOnlyList<(int Start, int End)> _ranges;
    private readonly IReadOnlySet<int>? _headerLines;
    private readonly int _maxLength;

    /// <param name="ranges">Sorted by start; may overlap or span lines.</param>
    public HighlightTransformer(IReadOnlyList<(int Start, int End)> ranges, IReadOnlySet<int>? headerLines = null)
    {
        _ranges = ranges;
        _headerLines = headerLines;
        _maxLength = ranges.Count == 0 ? 0 : ranges.Max(r => r.End - r.Start);
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_headerLines?.Contains(line.LineNumber) == true && line.Length > 0)
        {
            ChangeLinePart(
                line.Offset,
                line.EndOffset,
                e =>
                    e.TextRunProperties.SetTypeface(
                        new Typeface(e.TextRunProperties.Typeface.FontFamily, FontStyle.Normal, FontWeight.Bold)
                    )
            );
        }

        // No range longer than _maxLength exists, so anything starting before this cannot reach the line.
        for (var i = LowerBound(line.Offset - _maxLength); i < _ranges.Count; i++)
        {
            var (start, end) = _ranges[i];
            if (start >= line.EndOffset)
            {
                break;
            }

            var from = Math.Max(start, line.Offset);
            var to = Math.Min(end, line.EndOffset);
            if (to > from)
            {
                ChangeLinePart(from, to, e => e.TextRunProperties.SetBackgroundBrush(MatchBrush));
            }
        }
    }

    private int LowerBound(int offset)
    {
        int lo = 0,
            hi = _ranges.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_ranges[mid].Start < offset)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}
