namespace MonitorControl.App.UI;

/// <summary>Segoe Fluent Icons / Segoe MDL2 Assets glyphs.</summary>
internal static class Icons
{
    public const string Brightness = "";
    public const string Volume = "";
    public const string Mute = "";
    public const string Contrast = "";
    public const string Settings = "";
    public const string Update = "";
    public const string Quit = "";
    public const string Display = "";
    public const string Laptop = "";
    public const string Virtual = "";
    public const string Warning = "";

    /// <summary>Single installed icon font name (System.Drawing does not support fallback lists).</summary>
    public static string InstalledFontName { get; } = FindInstalledFont();

    private static string FindInstalledFont()
    {
        using var fonts = new System.Drawing.Text.InstalledFontCollection();
        return fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }
}
