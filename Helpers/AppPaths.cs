using System.IO;

namespace TaskbarTunes.Helpers;

/// <summary>
/// Rutas de los datos del usuario, todas bajo %APPDATA%\TaskbarTunes.
/// Incluye la migración desde la carpeta del nombre anterior (WidgetMusic).
/// </summary>
public static class AppPaths
{
    private const string FolderName = "TaskbarTunes";
    private const string LegacyFolderName = "WidgetMusic";

    private static readonly string AppData =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static string Dir { get; } = Path.Combine(AppData, FolderName);

    public static string SettingsFile => Path.Combine(Dir, "settings.json");
    public static string HistoryFile => Path.Combine(Dir, "history.json");
    public static string PresetsDir => Path.Combine(Dir, "presets");
    public static string ErrorLogFile => Path.Combine(Dir, "error.log");

    /// <summary>
    /// Copia los datos de %APPDATA%\WidgetMusic la primera vez que arranca la app
    /// renombrada, para no perder ajustes, historial ni presets. Copia en vez de
    /// mover: si vuelves a un binario antiguo, sus datos siguen ahí.
    /// </summary>
    public static void MigrateLegacyData()
    {
        try
        {
            if (Directory.Exists(Dir)) return; // ya migrado (o instalación nueva ya usada)

            var legacy = Path.Combine(AppData, LegacyFolderName);
            if (!Directory.Exists(legacy)) return;

            CopyTree(legacy, Dir);
        }
        catch
        {
            // Migración best-effort: si falla, la app arranca con valores por defecto.
        }
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: false);

        foreach (var dir in Directory.GetDirectories(from))
            CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
