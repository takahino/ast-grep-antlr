namespace AstGrepAntlr.Tests.Harness;

/// <summary>Disposable temporary directory for file-based tests.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir(string? prefix = null)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            (prefix ?? "ag4-test") + "-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Path);
    }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string WriteText(string relativePath, string content)
    {
        var full = Combine(relativePath);
        var dir = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(full, content);
        return full;
    }

    public string WriteBytes(string relativePath, byte[] bytes)
    {
        var full = Combine(relativePath);
        var dir = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(full, bytes);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; temp folder will be reclaimed by the OS.
        }
    }
}
