using System.IO;
using System.Text.Json;
using TaskbarTunes.Helpers;
using TaskbarTunes.Models;

namespace TaskbarTunes.Services;

/// <summary>
/// Temas integrados y presets del usuario. Un preset guarda solo la parte de
/// apariencia de <see cref="AppSettings"/> (colores, visualizador, tipografía),
/// nunca la posición ni el comportamiento. Los presets propios se guardan como
/// JSON en %APPDATA%\TaskbarTunes\presets\.
/// </summary>
public class PresetService
{
    private static readonly string Dir = AppPaths.PresetsDir;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Copia los campos de apariencia de un ajuste a otro.</summary>
    public static void CopyAppearance(AppSettings from, AppSettings to)
    {
        to.BackgroundColor = from.BackgroundColor;
        to.TextColor = from.TextColor;
        to.CornerRadius = from.CornerRadius;
        to.ShowVisualizer = from.ShowVisualizer;
        to.VisualizerStyle = from.VisualizerStyle;
        to.VisualizerBarCount = from.VisualizerBarCount;
        to.VisualizerOpacity = from.VisualizerOpacity;
        to.VisualizerColor = from.VisualizerColor;
        to.VisualizerColor2 = from.VisualizerColor2;
        to.VisualizerGradient = from.VisualizerGradient;
        to.VisualizerBarGap = from.VisualizerBarGap;
        to.GradientDirection = from.GradientDirection;
        to.BeatMode = from.BeatMode;
        to.AdaptiveColors = from.AdaptiveColors;
        to.FontFamily = from.FontFamily;
        to.TitleFontSize = from.TitleFontSize;
        to.AcrylicBackground = from.AcrylicBackground;
        to.AlbumArtStyle = from.AlbumArtStyle;
        to.CrossfadeArt = from.CrossfadeArt;
    }

    // ----- Temas integrados -----

    public static readonly (string Name, Action<AppSettings> Apply)[] BuiltIn =
    {
        ("Spotify", s =>
        {
            s.BackgroundColor = "#66151515"; s.TextColor = "#FFFFFFFF"; s.CornerRadius = 10;
            s.VisualizerStyle = "Bars"; s.VisualizerColor = "#FF1DB954"; s.VisualizerColor2 = "#FF17E86B";
            s.VisualizerGradient = true; s.GradientDirection = "Vertical";
            s.VisualizerOpacity = 0.55; s.VisualizerBarGap = 2; s.AdaptiveColors = false;
        }),
        ("Neón", s =>
        {
            s.BackgroundColor = "#59050014"; s.TextColor = "#FFF2EAFF"; s.CornerRadius = 12;
            s.VisualizerStyle = "MirrorBars"; s.VisualizerColor = "#FFFF00E6"; s.VisualizerColor2 = "#FF00E5FF";
            s.VisualizerGradient = true; s.GradientDirection = "Horizontal";
            s.VisualizerOpacity = 0.7; s.VisualizerBarGap = 2; s.AdaptiveColors = false;
        }),
        ("Monocromo", s =>
        {
            s.BackgroundColor = "#4D000000"; s.TextColor = "#FFFFFFFF"; s.CornerRadius = 8;
            s.VisualizerStyle = "Wave"; s.VisualizerColor = "#FFFFFFFF"; s.VisualizerColor2 = "#FFBBBBBB";
            s.VisualizerGradient = false; s.VisualizerOpacity = 0.3; s.AdaptiveColors = false;
        }),
        ("Ámbar retro", s =>
        {
            s.BackgroundColor = "#80000000"; s.TextColor = "#FFFFC163"; s.CornerRadius = 6;
            s.VisualizerStyle = "Leds"; s.VisualizerColor = "#FFFFA500"; s.VisualizerColor2 = "#FFFF3C00";
            s.VisualizerGradient = true; s.GradientDirection = "Vertical";
            s.VisualizerOpacity = 0.8; s.VisualizerBarGap = 2; s.AdaptiveColors = false;
        }),
        ("Adaptativo", s =>
        {
            s.BackgroundColor = "#59101010"; s.TextColor = "#FFFFFFFF"; s.CornerRadius = 10;
            s.VisualizerStyle = "FilledWave"; s.VisualizerOpacity = 0.6;
            s.VisualizerGradient = true; s.AdaptiveColors = true;
        }),
    };

    public static bool ApplyBuiltIn(string name, AppSettings target)
    {
        foreach (var (n, apply) in BuiltIn)
        {
            if (n == name) { apply(target); return true; }
        }
        return false;
    }

    // ----- Presets del usuario -----

    public IReadOnlyList<string> ListCustom()
    {
        try
        {
            if (!Directory.Exists(Dir)) return Array.Empty<string>();
            return Directory.GetFiles(Dir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n)
                .ToList()!;
        }
        catch { return Array.Empty<string>(); }
    }

    public bool SaveCustom(string name, AppSettings current)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(PathFor(name), JsonSerializer.Serialize(current, JsonOptions));
            return true;
        }
        catch { return false; }
    }

    public bool ApplyCustom(string name, AppSettings target)
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PathFor(name)));
            if (loaded is null) return false;
            CopyAppearance(loaded, target);
            return true;
        }
        catch { return false; }
    }

    public void DeleteCustom(string name)
    {
        try { File.Delete(PathFor(name)); } catch { }
    }

    private static string PathFor(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return Path.Combine(Dir, name + ".json");
    }
}
