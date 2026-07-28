using System.IO;
using System.Text.Json;
using TaskbarTunes.Helpers;

namespace TaskbarTunes.Services;

public class HistoryEntry
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime When { get; set; }
}

/// <summary>
/// Historial local de las últimas canciones (máx. 50), persistido en
/// %APPDATA%\TaskbarTunes\history.json. Sin red, sin telemetría.
/// </summary>
public class HistoryService
{
    private const int MaxEntries = 50;

    private static readonly string FilePath = AppPaths.HistoryFile;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly List<HistoryEntry> _items = new();
    private readonly object _lock = new();

    public IReadOnlyList<HistoryEntry> Items
    {
        get { lock (_lock) return _items.ToList(); }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var loaded = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath));
            if (loaded is not null)
                lock (_lock) { _items.Clear(); _items.AddRange(loaded.Take(MaxEntries)); }
        }
        catch { /* historial corrupto: empezar de cero */ }
    }

    public void Add(TrackInfo track)
    {
        if (string.IsNullOrWhiteSpace(track.Title)) return;

        lock (_lock)
        {
            // Dedupe: la misma canción repetida seguida no se apunta otra vez
            if (_items.Count > 0 && _items[0].Title == track.Title && _items[0].Artist == track.Artist)
                return;

            _items.Insert(0, new HistoryEntry
            {
                Title = track.Title,
                Artist = track.Artist,
                Source = track.SourceName,
                When = DateTime.Now,
            });
            if (_items.Count > MaxEntries)
                _items.RemoveRange(MaxEntries, _items.Count - MaxEntries);
        }
        Save();
    }

    public void Clear()
    {
        lock (_lock) _items.Clear();
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            List<HistoryEntry> snapshot;
            lock (_lock) snapshot = _items.ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(snapshot, JsonOptions));
        }
        catch { }
    }
}
