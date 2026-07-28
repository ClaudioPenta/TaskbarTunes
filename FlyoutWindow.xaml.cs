using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TaskbarTunes.Controls;
using TaskbarTunes.Helpers;
using TaskbarTunes.Services;

namespace TaskbarTunes;

/// <summary>
/// Panel expandido: carátula grande, título completo, barra de tiempo con seek
/// y controles. Se abre con un clic en el widget y se cierra al perder el foco.
/// </summary>
public partial class FlyoutWindow : Window
{
    private readonly SettingsService _settings;
    private readonly MediaSessionService _media;
    private readonly DispatcherTimer _timer;
    private bool _seeking;

    public FlyoutWindow(SettingsService settings, MediaSessionService media)
    {
        InitializeComponent();
        _settings = settings;
        _media = media;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += (_, _) => RefreshTimeline();

        _media.TrackChanged += OnTrack;
        Deactivated += (_, _) => HideFlyout();

        SeekSlider.AddHandler(Thumb.DragStartedEvent,
            new DragStartedEventHandler((_, _) => _seeking = true));
        SeekSlider.AddHandler(Thumb.DragCompletedEvent,
            new DragCompletedEventHandler((_, _) =>
            {
                _media.SeekToFraction(SeekSlider.Value);
                _seeking = false;
            }));
        // Clic directo en la pista del slider (sin arrastrar el pulgar)
        SeekSlider.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (!_seeking) _media.SeekToFraction(SeekSlider.Value);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TaskbarPositioner.MakeToolWindow(this, noActivate: false); // necesita foco para autocerrarse
    }

    public void ShowFlyout(double leftDip, double topDip)
    {
        ApplyAppearance();
        UpdateTrackUi(_media.LastTrack);
        RefreshTimeline();

        Left = leftDip;
        Top = topDip;
        Show();
        Activate();
        _timer.Start();
    }

    public void HideFlyout()
    {
        _timer.Stop();
        Hide();
    }

    private void ApplyAppearance()
    {
        var s = _settings.Settings;
        var bg = AudioVisualizer.ParseColor(s.BackgroundColor, Color.FromArgb(0xF0, 0x20, 0x20, 0x20));
        // El panel necesita más opacidad que el widget para leerse bien
        var solid = Color.FromArgb(Math.Max(bg.A, (byte)0xE8), bg.R, bg.G, bg.B);

        if (s.AcrylicBackground)
        {
            AcrylicHelper.Apply(this, true, Color.FromArgb(0xCC, bg.R, bg.G, bg.B));
            FlyRoot.Background = Brushes.Transparent;
        }
        else
        {
            AcrylicHelper.Apply(this, false, solid);
            FlyRoot.Background = new SolidColorBrush(solid);
        }

        var text = new SolidColorBrush(AudioVisualizer.ParseColor(s.TextColor, Colors.White));
        text.Freeze();
        FlyTitle.Foreground = text;
        FlyArtist.Foreground = text;
        FlySource.Foreground = text;
        FlyArtPlaceholder.Foreground = text;
        PosText.Foreground = text;
        DurText.Foreground = text;
        FlyPrev.Foreground = text;
        FlyPlay.Foreground = text;
        FlyNext.Foreground = text;

        var font = new FontFamily($"{s.FontFamily}, Segoe UI");
        FlyTitle.FontFamily = font;
        FlyArtist.FontFamily = font;
    }

    private void OnTrack(TrackInfo? info)
    {
        if (IsVisible) UpdateTrackUi(info);
    }

    private void UpdateTrackUi(TrackInfo? info)
    {
        if (info is null)
        {
            FlyTitle.Text = "Sin reproducción";
            FlyArtist.Text = "";
            FlySource.Text = "";
            FlyArt.Source = null;
            FlyArtPlaceholder.Visibility = Visibility.Visible;
            FlyPlay.Content = ""; // play
        }
        else
        {
            FlyTitle.Text = string.IsNullOrWhiteSpace(info.Title) ? "(Sin título)" : info.Title;
            FlyArtist.Text = info.Artist;
            FlySource.Text = info.SourceName;
            FlyArt.Source = info.AlbumArt;
            FlyArtPlaceholder.Visibility = info.AlbumArt is null ? Visibility.Visible : Visibility.Collapsed;
            FlyPlay.Content = info.IsPlaying ? "" : ""; // pausa / play
        }
    }

    private void RefreshTimeline()
    {
        var snap = _media.GetTimelineSnapshot();
        if (snap is null)
        {
            SeekSlider.IsEnabled = false;
            if (!_seeking) SeekSlider.Value = 0;
            PosText.Text = "--:--";
            DurText.Text = "--:--";
            return;
        }

        var (fraction, position, duration) = snap.Value;
        SeekSlider.IsEnabled = true;
        if (!_seeking) SeekSlider.Value = fraction;
        PosText.Text = Format(position);
        DurText.Text = Format(duration);
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    private void OnPlayPause(object sender, RoutedEventArgs e) => _media.TogglePlayPause();
    private void OnPrev(object sender, RoutedEventArgs e) => _media.SkipPrevious();
    private void OnNext(object sender, RoutedEventArgs e) => _media.SkipNext();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) HideFlyout();
    }

    protected override void OnClosed(EventArgs e)
    {
        _media.TrackChanged -= OnTrack;
        base.OnClosed(e);
    }
}
