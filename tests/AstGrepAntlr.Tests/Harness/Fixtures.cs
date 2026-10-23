namespace AstGrepAntlr.Tests.Harness;

public static class Fixtures
{
    /// <summary>Repo-root-relative fixtures directory (works under `dotnet test` cwd = test project).</summary>
    public static string Root
    {
        get
        {
            var fromOutput = Path.Combine(AppContext.BaseDirectory, "fixtures");
            if (Directory.Exists(fromOutput))
            {
                return fromOutput;
            }

            // Walk up from cwd looking for tests/fixtures.
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "tests", "fixtures");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("tests/fixtures not found.");
        }
    }

    public static string CalcLexerInterp => Path.Combine(Root, "calc", "CalcLexer.interp");
    public static string CalcParserInterp => Path.Combine(Root, "calc", "CalcParser.interp");

    public static string CasesDir(string lang) => Path.Combine(Root, "cases", lang);

    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AstGrepAntlr.slnx")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Repository root (AstGrepAntlr.slnx) not found.");
        }
    }
}
