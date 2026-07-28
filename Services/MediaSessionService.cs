using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using TaskbarTunes.Helpers;

namespace TaskbarTunes.Services;

public enum SourceKind { Spotify, Browser, Other }

public class TrackInfo
{
    public string Title = "";
    public string Artist = "";
    public string SourceName = "";
    public SourceKind Kind = SourceKind.Other;
    public bool IsPlaying;
    public BitmapImage? AlbumArt;
}

/// <summary>
/// Lee la música en reproducción desde los Global System Media Transport Controls
/// de Windows (la misma fuente que el flyout de volumen). Cubre Spotify de
/// escritorio y navegadores (YouTube, Spotify Web...) sin APIs externas.
/// </summary>
public class MediaSessionService : IDisposable
{
    private static readonly string[] BrowserKeywords =
        { "chrome", "msedge", "edge", "firefox", "opera", "brave", "vivaldi", "zen", "arc", "chromium" };

    private readonly Dispatcher _dispatcher;
    private readonly SettingsService _settings;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _current;
    private int _version; // descarta actualizaciones asíncronas obsoletas

    /// <summary>Null cuando no hay ninguna sesión de medios activa.</summary>
    public event Action<TrackInfo?>? TrackChanged;

    public TrackInfo? LastTrack { get; private set; }

    public MediaSessionService(Dispatcher dispatcher, SettingsService settings)
    {
        _dispatcher = dispatcher;
        _settings = settings;
    }

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => _dispatcher.BeginInvoke(Repick);
        _manager.SessionsChanged += (_, _) => _dispatcher.BeginInvoke(Repick);
        Repick();
    }

    private static SourceKind Classify(GlobalSystemMediaTransportControlsSession session)
    {
        string id = (session.SourceAppUserModelId ?? "").ToLowerInvariant();
        if (id.Contains("spotify")) return SourceKind.Spotify;
        if (BrowserKeywords.Any(id.Contains)) return SourceKind.Browser;
        return SourceKind.Other;
    }

    /// <summary>Reelige la sesión a mostrar (llamar también cuando cambie la fuente preferida).</summary>
    public void Repick()
    {
        if (_manager is null) return;

        List<GlobalSystemMediaTransportControlsSession> sessions;
        try { sessions = _manager.GetSessions().ToList(); }
        catch { return; }

        IReadOnlyList<GlobalSystemMediaTransportControlsSession> pool = sessions;
        string pref = _settings.Settings.PreferredSource;
        if (pref is "Spotify" or "Browser")
        {
            var kind = pref == "Spotify" ? SourceKind.Spotify : SourceKind.Browser;
            var filtered = sessions.Where(s => Classify(s) == kind).ToList();
            if (filtered.Count > 0) pool = filtered;
        }

        GlobalSystemMediaTransportControlsSession? chosen = null;
        if (pool.Count > 0)
        {
            chosen = pool.FirstOrDefault(s =>
                SafeStatus(s) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);

            if (chosen is null)
            {
                var currentId = SafeCall(() => _manager.GetCurrentSession()?.SourceAppUserModelId);
                chosen = pool.FirstOrDefault(s => s.SourceAppUserModelId == currentId) ?? pool[0];
            }
        }

        if (!ReferenceEquals(chosen, _current))
        {
            Unhook(_current);
            _current = chosen;
            Hook(_current);
        }

        UpdateAsync();
    }

    private void Hook(GlobalSystemMediaTransportControlsSession? session)
    {
        if (session is null) return;
        session.MediaPropertiesChanged += OnSessionChanged;
        session.PlaybackInfoChanged += OnSessionChanged;
    }

    private void Unhook(GlobalSystemMediaTransportControlsSession? session)
    {
        if (session is null) return;
        try
        {
            session.MediaPropertiesChanged -= OnSessionChanged;
            session.PlaybackInfoChanged -= OnSessionChanged;
        }
        catch { /* la sesión puede haber muerto ya */ }
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args)
        => _dispatcher.BeginInvoke(UpdateAsync);

    private async void UpdateAsync()
    {
        int version = Interlocked.Increment(ref _version);
        var session = _current;

        if (session is null)
        {
            Raise(version, null);
            return;
        }

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var kind = Classify(session);

            var info = new TrackInfo
            {
                Title = props.Title ?? "",
                Artist = props.Artist ?? "",
                Kind = kind,
                SourceName = kind switch
                {
                    SourceKind.Spotify => "Spotify",
                    SourceKind.Browser => "Navegador",
                    _ => session.SourceAppUserModelId ?? "",
                },
                IsPlaying = playback.PlaybackStatus ==
                            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            };

            if (kind == SourceKind.Browser && _settings.Settings.CleanYouTubeTitles)
                (info.Title, info.Artist) = TrackTitleCleaner.Clean(info.Title, info.Artist);

            if (props.Thumbnail is not null)
            {
                try
                {
                    using var winrtStream = await props.Thumbnail.OpenReadAsync();
                    using var stream = winrtStream.AsStreamForRead();
                    var ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    ms.Position = 0;

                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.DecodePixelWidth = 96;
                    bmp.EndInit();
                    bmp.Freeze(); // usable desde el hilo de UI aunque se creó aquí
                    info.AlbumArt = bmp;
                }
                catch { /* sin carátula */ }
            }

            Raise(version, info);
        }
        catch
        {
            // La sesión murió a mitad de lectura; la próxima SessionsChanged reelegirá.
        }
    }

    private void Raise(int version, TrackInfo? info)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (version != _version) return; // llegó una actualización más nueva
            LastTrack = info;
            TrackChanged?.Invoke(info);
        });
    }

    public async void TogglePlayPause() => await SafeControl(s => s.TryTogglePlayPauseAsync());
    public async void SkipNext() => await SafeControl(s => s.TrySkipNextAsync());
    public async void SkipPrevious() => await SafeControl(s => s.TrySkipPreviousAsync());

    /// <summary>Tipo de la fuente actual, o null si no hay sesión.</summary>
    public SourceKind? CurrentKind => _current is null ? null : Classify(_current);

    /// <summary>AUMID de la app de la sesión actual (para la captura de audio por proceso).</summary>
    public string? CurrentSourceAppId => SafeCall(() => _current?.SourceAppUserModelId);

    /// <summary>Progreso actual de la pista (0..1) o null si la fuente no reporta duración.</summary>
    public double? GetProgress() => GetTimelineSnapshot()?.Fraction;

    /// <summary>
    /// Posición y duración actuales, o null si la fuente no reporta duración.
    /// Extrapola con el reloj desde la última actualización, porque las apps
    /// solo notifican la posición de vez en cuando.
    /// </summary>
    public (double Fraction, TimeSpan Position, TimeSpan Duration)? GetTimelineSnapshot()
    {
        var session = _current;
        if (session is null) return null;
        try
        {
            var tl = session.GetTimelineProperties();
            var duration = tl.EndTime - tl.StartTime;
            if (duration.TotalSeconds < 1) return null;

            var pos = tl.Position;
            if (SafeStatus(session) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                pos += DateTimeOffset.UtcNow - tl.LastUpdatedTime;

            var relative = pos - tl.StartTime;
            if (relative < TimeSpan.Zero) relative = TimeSpan.Zero;
            if (relative > duration) relative = duration;

            return (relative.TotalSeconds / duration.TotalSeconds, relative, duration);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Salta a una fracción (0..1) de la pista, si la app lo permite.</summary>
    public async void SeekToFraction(double fraction)
    {
        var session = _current;
        if (session is null) return;
        try
        {
            var tl = session.GetTimelineProperties();
            var duration = tl.EndTime - tl.StartTime;
            if (duration.TotalSeconds < 1) return;

            long ticks = (tl.StartTime + duration * Math.Clamp(fraction, 0, 1)).Ticks;
            await session.TryChangePlaybackPositionAsync(ticks);
        }
        catch { /* la fuente no soporta seek */ }
    }

    private async Task SafeControl(
        Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> action)
    {
        var session = _current;
        if (session is null) return;
        try { await action(session); }
        catch { /* la app de origen se cerró */ }
    }

    private static GlobalSystemMediaTransportControlsSessionPlaybackStatus SafeStatus(
        GlobalSystemMediaTransportControlsSession s)
    {
        try { return s.GetPlaybackInfo().PlaybackStatus; }
        catch { return GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed; }
    }

    private static T? SafeCall<T>(Func<T?> f)
    {
        try { return f(); }
        catch { return default; }
    }

    public void Dispose() => Unhook(_current);
}
