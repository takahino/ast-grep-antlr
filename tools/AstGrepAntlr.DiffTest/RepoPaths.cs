namespace AstGrepAntlr.DiffTest;

internal static class RepoPaths
{
    public static string FindRepositoryRoot(string? start = null)
    {
        var dir = new DirectoryInfo(start ?? Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AstGrepAntlr.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate repository root (AstGrepAntlr.slnx not found in parent directories)."
        );
    }
}
