using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Ag4Gui;

internal static class Ag4Native
{
    private const string DllName = "ag4_native";
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        var dir = ResolveLibDir();
        var path = Path.Combine(dir, OperatingSystem.IsWindows() ? "ag4_native.dll" : "ag4_native.so");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Native library not found at '{path}'. Publish with:\n"
                    + "  dotnet publish src/AstGrepAntlr.Native -c Release -r win-x64 -o samples/gui/lib\n"
                    + "Or set AG4_NATIVE_DIR.",
                path
            );
        }

        NativeLibrary.Load(path);
        _loaded = true;
    }

    public static string Version()
    {
        EnsureLoaded();
        var p = ag4_version();
        return Marshal.PtrToStringUTF8(p) ?? string.Empty;
    }

    public static (int Code, string Json) Search(string requestJson)
    {
        EnsureLoaded();
        var rc = ag4_search(requestJson, out var outPtr);
        try
        {
            var json = outPtr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(outPtr) ?? string.Empty;
            return (rc, json);
        }
        finally
        {
            if (outPtr != IntPtr.Zero)
            {
                ag4_free(outPtr);
            }
        }
    }

    public static string ResolveLibDir()
    {
        var env = Environment.GetEnvironmentVariable("AG4_NATIVE_DIR");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
        {
            return Path.GetFullPath(env);
        }

        // samples/gui/csharp/bin/... → samples/gui/lib
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "lib");
            if (File.Exists(Path.Combine(candidate, "ag4_native.dll")))
            {
                return candidate;
            }

            if (dir.Name.Equals("gui", StringComparison.OrdinalIgnoreCase))
            {
                candidate = Path.Combine(dir.FullName, "lib");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            dir = dir.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "lib"));
    }

    public static string DefaultFixturesDir()
    {
        var lib = ResolveLibDir();
        var fixtures = Path.GetFullPath(Path.Combine(lib, "..", "fixtures"));
        return Directory.Exists(fixtures) ? fixtures : lib;
    }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ag4_version();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ag4_search([MarshalAs(UnmanagedType.LPUTF8Str)] string requestJson, out IntPtr outJson);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ag4_free(IntPtr buffer);
}

internal sealed class MatchRow
{
    public string? Text { get; set; }
    public string? File { get; set; }
    public RangeRow? Range { get; set; }
}

internal sealed class RangeRow
{
    public LineColRow? Start { get; set; }
}

internal sealed class LineColRow
{
    public int Line { get; set; }
    public int Column { get; set; }
}

internal static class MatchParser
{
    public static List<MatchRow> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out _))
        {
            throw new InvalidOperationException(doc.RootElement.GetProperty("error").GetString() ?? "error");
        }

        return JsonSerializer.Deserialize<List<MatchRow>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            ) ?? [];
    }
}
