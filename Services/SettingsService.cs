using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using TaskbarTunes.Helpers;
using TaskbarTunes.Models;

namespace TaskbarTunes.Services;

/// <summary>
/// Carga y guarda la configuración en %APPDATA%\TaskbarTunes\settings.json.
/// El guardado se agrupa (debounce) para no escribir en cada tick de un slider.
/// </summary>
public class SettingsService
{
    private static readonly string Dir = AppPaths.Dir;

    private static readonly string FilePath = AppPaths.SettingsFile;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly DispatcherTimer _saveTimer;

    public AppSettings Settings { get; private set; } = new();

    /// <summary>Se dispara cuando la configuración cambia (para aplicar en vivo).</summary>
    public event Action? Changed;

    public SettingsService()
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); };
    }

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();

            // Migración: configs anteriores a FreePlaced usaban -1 como "sin fijar"
            if (!Settings.FreePlaced && (Settings.FreeX >= 0 || Settings.FreeY >= 0))
                Settings.FreePlaced = true;
        }
        catch
        {
            Settings = new AppSettings(); // JSON corrupto: volver a valores por defecto
        }
    }

    /// <summary>Notifica un cambio: aplica en vivo ya y programa el guardado a disco.</summary>
    public void NotifyChanged()
    {
        Changed?.Invoke();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Settings, JsonOptions));
        }
        catch
        {
            // Sin acceso a disco: la app sigue funcionando con la config en memoria.
        }
    }
}
