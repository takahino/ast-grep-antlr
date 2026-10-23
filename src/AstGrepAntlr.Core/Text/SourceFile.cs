using System.Text;

namespace AstGrepAntlr.Text;

/// <summary>Source file loaded as raw bytes with detected encoding.</summary>
public sealed class SourceFile
{
    public required string Path { get; init; }
    public required byte[] Bytes { get; init; }
    public required DetectedEncoding Detected { get; init; }
    public required string Text { get; init; }
    public required string Hash { get; init; }

    public string EncodingName => Detected.Name;

    public static SourceFile Read(string path, string? explicitEncoding = null)
    {
        var bytes = File.ReadAllBytes(path);
        var detected = EncodingDetector.Detect(bytes, explicitEncoding);
        var text = EncodingDetector.Decode(bytes, detected);
        return new SourceFile
        {
            Path = path,
            Bytes = bytes,
            Detected = detected,
            Text = text,
            Hash = EncodingDetector.Sha256Hex(bytes),
        };
    }

    public static bool TryRead(string path, out SourceFile? file, out string? error, string? explicitEncoding = null)
    {
        try
        {
            file = Read(path, explicitEncoding);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is DecoderFallbackException or ArgumentException or NotSupportedException)
        {
            file = null;
            error = ex.Message;
            return false;
        }
    }
}
