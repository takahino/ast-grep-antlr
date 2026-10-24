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
            var req = JsonSerializer.Serialize(
                new
                {
                    pattern = "foo($$$ARGS)",
                    language = "java",
                    paths = new[] { fixtures },
                    maxResults = 10,
                }
            );
            var (code, json) = Ag4Native.Search(req);
            Console.WriteLine($"rc={code}");
            Console.WriteLine(json);
            if (code != 0)
            {
                return 1;
            }

            var rows = MatchParser.Parse(json);
            Console.WriteLine($"matches={rows.Count}");
            return rows.Count > 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 3;
        }
    }
}
