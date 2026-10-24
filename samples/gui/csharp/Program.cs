using System.Text.Json;

namespace Ag4Gui;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--smoke"])
        {
            return Smoke();
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static int Smoke()
    {
        try
        {
            Console.WriteLine($"version={Ag4Native.Version()}");
            var fixtures = Ag4Native.DefaultFixturesDir();
            var pathReq = JsonSerializer.Serialize(
                new
                {
                    pattern = "foo($$$ARGS)",
                    language = "java",
                    paths = new[] { fixtures },
                    maxResults = 10,
                }
            );
            var (pathCode, pathJson) = Ag4Native.Search(pathReq);
            Console.WriteLine($"paths rc={pathCode}");
            Console.WriteLine(pathJson);
            if (pathCode != 0)
            {
                return 1;
            }

            var pathRows = MatchParser.Parse(pathJson);
            Console.WriteLine($"paths matches={pathRows.Count}");
            if (pathRows.Count == 0)
            {
                return 2;
            }

            var sourceReq = JsonSerializer.Serialize(
                new
                {
                    pattern = "foo($$$ARGS)",
                    language = "java",
                    sources = new[]
                    {
                        new { path = "snippet.java", content = "class Demo { void m() { foo(1, 2); } }\n" },
                    },
                    maxResults = 10,
                }
            );
            var (srcCode, srcJson) = Ag4Native.Search(sourceReq);
            Console.WriteLine($"sources rc={srcCode}");
            Console.WriteLine(srcJson);
            if (srcCode != 0)
            {
                return 1;
            }

            var srcRows = MatchParser.Parse(srcJson);
            Console.WriteLine($"sources matches={srcRows.Count}");
            return srcRows.Count > 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 3;
        }
    }
}
