using System.Text.Json;
using System.Text.Json.Serialization;
using AstGrepAntlr.Gui.Models;

namespace AstGrepAntlr.Gui.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path =
            path
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ag4-gui",
                "settings.json"
            );
    }

    public string FilePath => _path;

    /// <summary>Returns defaults (and the reason) when the file is missing or unreadable, so startup never fails.</summary>
    public (AppSettings Settings, string? Error) Load()
    {
        if (!File.Exists(_path))
        {
            return (new AppSettings(), null);
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions);
            return (settings ?? new AppSettings(), null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return (new AppSettings(), $"{_path}: {ex.Message}");
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // Write-then-move so a crash mid-write never leaves a truncated settings file.
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tmp, _path, overwrite: true);
    }
}
