using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrameColoringV2.App;

public sealed class PaletteEntry
{
    public string Name { get; set; } = "Color";
    public string Hex { get; set; } = "#ffffff";
}

/// <summary>
/// User settings, stored in %AppData%\FrameColoringV2\settings.json so the app keeps
/// working no matter which folder the executable sits in.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public const int MaxRecent = 10;

    public const string DefaultCheckerLight = "#464a50";
    public const string DefaultCheckerDark = "#3a3e44";
    public const int DefaultCheckerSquareSize = 8;

    public string? LastOpenFolder { get; set; }
    public string? LastSaveFolder { get; set; }
    public List<string> RecentFolders { get; set; } = new();
    public double Zoom { get; set; } = 1.0;
    public bool OnionSkin { get; set; } = true;
    public float OnionSkinOpacity { get; set; } = 0.4f;
    public int BrushSize { get; set; } = 4;
    public int FillTolerance { get; set; } = 2;
    public bool AutoNextFrame { get; set; }
    public string ExternalEditorPath { get; set; } = @"C:\Program Files\Adobe\Adobe Photoshop 2025\Photoshop.exe";
    public bool ReopenLastFolder { get; set; } = true;
    public int SidebarWidth { get; set; } = 360;

    // Transparency checkerboard behind the frame.
    public string CheckerLightColor { get; set; } = DefaultCheckerLight;
    public string CheckerDarkColor { get; set; } = DefaultCheckerDark;
    public int CheckerSquareSize { get; set; } = DefaultCheckerSquareSize;
    public List<PaletteEntry> Palette { get; set; } = DefaultPalette();

    [JsonIgnore]
    public static string SettingsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FrameColoringV2");

    [JsonIgnore]
    public static string SettingsPath => Path.Combine(SettingsFolder, "settings.json");

    public static List<PaletteEntry> DefaultPalette() => new()
    {
        new() { Hex = "#0000ff", Name = "Hair (changeable)" },
        new() { Hex = "#844f69", Name = "Leather" },
        new() { Hex = "#614a4a", Name = "Hair dark" },
        new() { Hex = "#aa9c95", Name = "Hair light" },
        new() { Hex = "#7a878f", Name = "Metal" },
        new() { Hex = "#dae9f3", Name = "Blade" },
        new() { Hex = "#503f36", Name = "Dark wood" },
        new() { Hex = "#a07755", Name = "Light wood" },
        new() { Hex = "#00ff00", Name = "Team color" },
        new() { Hex = "#ff0000", Name = "Team color dark" },
        new() { Hex = "#ffff00", Name = "Ex color 1" },
        new() { Hex = "#ff00ff", Name = "Ex color 2" },
        new() { Hex = "#edcbba", Name = "Skin" },
        new() { Hex = "#000000", Name = "Feet" },
        new() { Hex = "#ffffff", Name = "Shine" }
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions);
                if (loaded != null)
                {
                    if (loaded.Palette.Count == 0) loaded.Palette = DefaultPalette();
                    return loaded;
                }
            }
        }
        catch (Exception)
        {
            // A corrupt settings file should never stop the editor from starting.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // Settings are a convenience, never a reason to fail on exit.
        }
    }

    public void PushRecentFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;

        RecentFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        RecentFolders.Insert(0, folder);
        if (RecentFolders.Count > MaxRecent) RecentFolders.RemoveRange(MaxRecent, RecentFolders.Count - MaxRecent);
    }
}
