namespace AstGrepAntlr.Gui.Models;

/// <summary>Persisted between launches (settings.json).</summary>
public sealed class AppSettings
{
    public const int MaxHistory = 30;

    public SearchRequest Last { get; set; } = new();
    public List<string> History { get; set; } = [];

    /// <summary><c>ja</c> / <c>en</c>; null means follow the OS locale.</summary>
    public string? UiLanguage { get; set; }
    public bool Incremental { get; set; }
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 840;
}
