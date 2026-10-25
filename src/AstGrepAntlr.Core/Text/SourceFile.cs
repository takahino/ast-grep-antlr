using System.Text;

namespace AstGrepAntlr.Text;

/// <summary>Source file loaded as raw bytes with detected encoding.</summary>
public sealed class SourceFile
{
    private string? _hash;

    public required string Path { get; init; }
    public required byte[] Bytes { get; init; }
    public required DetectedEncoding Detected { get; init; }
    public required string Text { get; init; }

    /// <summary>SHA-256 of <see cref="Bytes"/>, computed on first access.</summary>
    public string Hash => _hash ??= EncodingDetector.Sha256Hex(Bytes);

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
        };
    }

    /// <summary>In-memory source (UTF-8) for embedding / frontend-collected buffers.</summary>
    public static SourceFile FromText(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var detected = EncodingDetector.FromName("utf-8");
        var bytes = EncodingDetector.Encode(text, detected);
        return new SourceFile
        {
            Path = string.IsNullOrEmpty(path) ? "<buffer>" : path,
            Bytes = bytes,
            Detected = detected,
            Text = text,
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
