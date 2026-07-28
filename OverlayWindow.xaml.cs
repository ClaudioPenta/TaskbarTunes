using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TaskbarTunes.Controls;
using TaskbarTunes.Helpers;
using TaskbarTunes.Services;
using WinForms = System.Windows.Forms;

namespace TaskbarTunes;

/// <summary>
/// La ventana-widget: sin bordes, transparente, anclada sobre la barra de tareas
/// (o flotante en modo Free). Un timer de 500 ms la mantiene posicionada,
/// topmost, actualiza el progreso y sigue el auto-ocultado de la barra.
/// Arrastrable con el ratón; un clic abre el panel expandido.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly SettingsService _settings;
    private readonly MediaSessionService _media;
    private readonly HistoryService _history;
    private readonly DispatcherTimer _anchorTimer;
    private readonly DispatcherTimer _clickTimer;
    private FlyoutWindow? _flyout;

    private bool _hasTrack;
    private bool _isPlaying;
    private SourceKind? _currentKind;
    private Color? _adaptiveMain, _adaptiveSecondary;

    // Carátula: crossfade y vinilo
    private bool _artOnB;
    private bool _spinning;
    private string _lastArtKey = "";

    // Estado del arrastre
    private bool _mouseDown, _dragging;
    private System.Drawing.Point _dragStartCursor;
    private int _dragStartX, _dragStartY;

    public OverlayWindow(SettingsService settings, MediaSessionService media,
        AudioCaptureService audio, HistoryService history)
    {
        InitializeComponent();
        _settings = settings;
        _media = media;
        _history = history;

        Visualizer.Attach(audio);
        _media.TrackChanged += UpdateTrack;

        _anchorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _anchorTimer.Tick += (_, _) => Anchor();

        _clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        _clickTimer.Tick += (_, _) => { _clickTimer.Stop(); ToggleFlyout(); };

        SizeChanged += (_, _) => UpdateMarquee();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TaskbarPositioner.MakeToolWindow(this);
        ApplySettings();
        Anchor();
        _anchorTimer.Start();
    }

    private void Anchor()
    {
        if (_dragging) return;
        if (WidgetMenu.IsOpen) return; // no tocar la ventana mientras el menú está abierto

        var s = _settings.Settings;
        if (s.HideWhenNoMusic && !_hasTrack)
        {
            if (Visibility == Visibility.Visible) Visibility = Visibility.Hidden;
            return;
        }

        bool shouldShow = TaskbarPositioner.Position(this, s);
        if (!shouldShow && Visibility == Visibility.Visible)
            Visibility = Visibility.Hidden;
        else if (shouldShow && Visibility != Visibility.Visible)
            Visibility = Visibility.Visible;

        UpdateProgress();
    }

    /// <summary>Aplica la configuración en vivo (colores, tamaño, posición, visualizador, tipografía).</summary>
    public void ApplySettings()
    {
        var s = _settings.Settings;

        RootBorder.CornerRadius = new CornerRadius(s.CornerRadius);
        var bgColor = AudioVisualizer.ParseColor(s.BackgroundColor, Color.FromArgb(0x66, 0x15, 0x15, 0x15));
        AcrylicHelper.Apply(this, s.AcrylicBackground, bgColor);
        RootBorder.Background = s.AcrylicBackground
            ? Brushes.Transparent // el tinte lo pone el acrílico
            : new SolidColorBrush(bgColor);

        var textBrush = MakeBrush(s.TextColor, Colors.White);
        TitleText.Foreground = textBrush;
        ArtistText.Foreground = textBrush;
        SourceIcon.Foreground = textBrush;
        ArtPlaceholder.Foreground = textBrush;
        PrevButton.Foreground = textBrush;
        PlayButton.Foreground = textBrush;
        NextButton.Foreground = textBrush;

        var font = new FontFamily($"{s.FontFamily}, Segoe UI");
        TitleText.FontFamily = font;
        ArtistText.FontFamily = font;
        TitleText.FontSize = Math.Clamp(s.TitleFontSize, 9, 18);
        ArtistText.FontSize = Math.Max(8.5, TitleText.FontSize - 1.5);

        ArtBorder.Visibility = s.ShowAlbumArt ? Visibility.Visible : Visibility.Collapsed;
        ControlsPanel.Visibility = s.ShowControls ? Visibility.Visible : Visibility.Collapsed;
        ArtistRow.Visibility = s.ShowArtist ? Visibility.Visible : Visibility.Collapsed;
        RefreshSourceIcon();
        UpdateVinyl();

        Visualizer.ApplySettings(s);
        Visualizer.SetAdaptiveColors(_adaptiveMain, _adaptiveSecondary);
        UpdateAccent();

        Anchor();
        Dispatcher.BeginInvoke(UpdateMarquee, DispatcherPriority.Loaded);
    }

    private static SolidColorBrush MakeBrush(string hex, Color fallback)
    {
        var brush = new SolidColorBrush(AudioVisualizer.ParseColor(hex, fallback));
        brush.Freeze();
        return brush;
    }

    private void UpdateTrack(TrackInfo? info)
    {
        _hasTrack = info is not null;
        _isPlaying = info?.IsPlaying == true;
        _currentKind = info?.Kind;

        // Colores adaptativos desde la carátula
        if (_settings.Settings.AdaptiveColors && info?.AlbumArt is not null &&
            ColorExtractor.FromBitmap(info.AlbumArt) is { } extracted)
        {
            (_adaptiveMain, _adaptiveSecondary) = (extracted.Main, extracted.Secondary);
        }
        else
        {
            (_adaptiveMain, _adaptiveSecondary) = (null, null);
        }
        Visualizer.SetAdaptiveColors(_adaptiveMain, _adaptiveSecondary);
        UpdateAccent();

        if (info is null)
        {
            TitleText.Text = "Sin reproducción";
            ArtistText.Text = "";
            SetArt(null, newTrack: true);
            _lastArtKey = "";
            ArtPlaceholder.Visibility = Visibility.Visible;
            PlayButton.Content = ""; // play
            RootBorder.ToolTip = null;
        }
        else
        {
            TitleText.Text = string.IsNullOrWhiteSpace(info.Title) ? "(Sin título)" : info.Title;
            ArtistText.Text = string.IsNullOrWhiteSpace(info.Artist)
                ? info.SourceName
                : info.Artist;

            string artKey = $"{info.Title}|{info.Artist}";
            SetArt(info.AlbumArt, newTrack: artKey != _lastArtKey);
            _lastArtKey = artKey;

            ArtPlaceholder.Visibility = info.AlbumArt is null ? Visibility.Visible : Visibility.Collapsed;
            PlayButton.Content = info.IsPlaying ? "" : ""; // pausa / play
            SourceIcon.Text = info.Kind switch
            {
                SourceKind.Spotify => "",  // nota musical
                SourceKind.Browser => "",  // globo
                _ => "",                   // altavoz
            };
            RootBorder.ToolTip = string.IsNullOrWhiteSpace(info.Artist)
                ? $"{info.Title} · {info.SourceName}"
                : $"{info.Title} — {info.Artist} · {info.SourceName}";
        }

        RefreshSourceIcon();
        UpdateVinyl();
        Anchor(); // por si HideWhenNoMusic cambió la visibilidad
        Dispatcher.BeginInvoke(UpdateMarquee, DispatcherPriority.Loaded);
    }

    // ----- Carátula: crossfade y vinilo -----

    private void SetArt(ImageSource? src, bool newTrack)
    {
        var front = _artOnB ? ArtImageB : ArtImageA;
        var back = _artOnB ? ArtImageA : ArtImageB;

        if (src is null)
        {
            front.BeginAnimation(OpacityProperty, null);
            back.BeginAnimation(OpacityProperty, null);
            ArtImageA.Source = null; ArtImageB.Source = null;
            ArtImageA.Opacity = 1; ArtImageB.Opacity = 0;
            _artOnB = false;
            return;
        }

        bool crossfade = _settings.Settings.CrossfadeArt && newTrack && front.Source is not null;
        if (!crossfade)
        {
            front.Source = src;
            return;
        }

        back.Source = src;
        var dur = TimeSpan.FromMilliseconds(320);
        back.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur));
        front.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, dur));
        _artOnB = !_artOnB;
    }

    private void UpdateVinyl()
    {
        var s = _settings.Settings;
        bool vinyl = s.AlbumArtStyle == "Vinyl" && s.ShowAlbumArt;
        ArtMaskShape.CornerRadius = new CornerRadius(vinyl ? 15 : 6);

        bool hasArt = ArtImageA.Source is not null || ArtImageB.Source is not null;
        VinylHole.Visibility = vinyl && hasArt ? Visibility.Visible : Visibility.Collapsed;

        bool shouldSpin = vinyl && hasArt && _isPlaying;
        if (shouldSpin && !_spinning)
        {
            _spinning = true;
            ArtRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(6)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else if (!shouldSpin && _spinning)
        {
            _spinning = false;
            ArtRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            ArtRotate.Angle = 0;
        }
    }

    private void RefreshSourceIcon() =>
        SourceIcon.Visibility = _settings.Settings.ShowSourceIcon && _hasTrack
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>Color de acento (barra de progreso): adaptativo o el principal del visualizador.</summary>
    private void UpdateAccent()
    {
        Color accent = _settings.Settings.AdaptiveColors && _adaptiveMain.HasValue
            ? _adaptiveMain.Value
            : AudioVisualizer.ParseColor(_settings.Settings.VisualizerColor, Color.FromRgb(0x1D, 0xB9, 0x54));
        var brush = new SolidColorBrush(accent);
        brush.Freeze();
        ProgressFill.Background = brush;
    }

    private void UpdateProgress()
    {
        if (!_settings.Settings.ShowProgress || !_hasTrack)
        {
            ProgressTrack.Visibility = Visibility.Collapsed;
            return;
        }

        double? progress = _media.GetProgress();
        if (progress is null)
        {
            ProgressTrack.Visibility = Visibility.Collapsed;
            return;
        }

        ProgressTrack.Visibility = Visibility.Visible;
        double w = ProgressTrack.ActualWidth;
        if (w > 0) ProgressFill.Width = Math.Max(0, w * progress.Value);
    }

    private void OnProgressMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_hasTrack || ProgressTrack.Visibility != Visibility.Visible) return;
        double f = e.GetPosition(ProgressHit).X / Math.Max(1, ProgressHit.ActualWidth);
        _media.SeekToFraction(Math.Clamp(f, 0, 1));
        e.Handled = true; // que no arranque el arrastre ni el clic-panel
    }

    /// <summary>Desplaza el título en vaivén si no cabe en su contenedor.</summary>
    private void UpdateMarquee()
    {
        TitleTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        TitleTranslate.X = 0;

        double overflow = TitleText.ActualWidth - TitleClip.ActualWidth;
        if (overflow > 4 && _hasTrack)
        {
            var anim = new DoubleAnimation(0, -(overflow + 6), TimeSpan.FromSeconds(Math.Max(2.5, overflow / 22)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(1.6),
            };
            TitleTranslate.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }

    // ----- Panel expandido -----

    private void ToggleFlyout()
    {
        _flyout ??= new FlyoutWindow(_settings, _media);
        if (_flyout.IsVisible)
        {
            _flyout.HideFlyout();
            return;
        }

        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var (x, y, w, h) = TaskbarPositioner.GetWindowBounds(this);
        var vs = TaskbarPositioner.GetVirtualScreenBounds();

        double fw = 360 * scale;
        double fh = 200 * scale; // estimación (SizeToContent ajusta el alto real)
        double fx = Math.Clamp(x + w / 2.0 - fw / 2, vs.Left, Math.Max(vs.Left, vs.Right - fw));
        double fy = y - fh - 10 * scale;
        if (fy < vs.Top) fy = y + h + 10 * scale;

        _flyout.ShowFlyout(fx / scale, fy / scale);
    }

    // ----- Arrastre, clic y doble clic -----

    private void OnRootMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _clickTimer.Stop();
            _mouseDown = false;
            ReleaseMouseCapture();
            DoDoubleClickAction();
            return;
        }

        _mouseDown = true;
        _dragging = false;
        _dragStartCursor = WinForms.Cursor.Position;
        (_dragStartX, _dragStartY, _, _) = TaskbarPositioner.GetWindowBounds(this);
        CaptureMouse();
    }

    private void OnRootMouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDown || e.LeftButton != MouseButtonState.Pressed) return;

        var cur = WinForms.Cursor.Position;
        int dx = cur.X - _dragStartCursor.X;
        int dy = cur.Y - _dragStartCursor.Y;
        if (!_dragging && Math.Abs(dx) < 5 && Math.Abs(dy) < 5) return;

        if (!_dragging)
        {
            _dragging = true;
            _clickTimer.Stop();
            Cursor = Cursors.SizeAll;
        }

        var s = _settings.Settings;
        var (_, curY, w, h) = TaskbarPositioner.GetWindowBounds(this);

        if (s.Position == "Free")
        {
            // Por todo el escritorio (incluidas otras pantallas)
            var vs = TaskbarPositioner.GetVirtualScreenBounds();
            int nx = Math.Clamp(_dragStartX + dx, vs.Left, Math.Max(vs.Left, vs.Right - w));
            int ny = Math.Clamp(_dragStartY + dy, vs.Top, Math.Max(vs.Top, vs.Bottom - h));
            TaskbarPositioner.MoveTo(this, nx, ny);
        }
        else
        {
            // En la barra: solo horizontal, de borde a borde (100% de la barra)
            var tb = TaskbarPositioner.GetTaskbarInfo(s.MonitorIndex);
            int min = tb?.Left ?? 0;
            int max = (tb?.Right ?? int.MaxValue) - w;
            int nx = Math.Clamp(_dragStartX + dx, min, Math.Max(min, max));
            TaskbarPositioner.MoveTo(this, nx, curY);
        }
    }

    private void OnRootMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseDown) return;
        _mouseDown = false;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;

        if (_dragging)
        {
            _dragging = false;

            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var (x, y, _, _) = TaskbarPositioner.GetWindowBounds(this);
            var s = _settings.Settings;

            if (s.Position == "Free")
            {
                s.FreeX = x / scale;
                s.FreeY = y / scale;
                s.FreePlaced = true;
            }
            else
            {
                var tb = TaskbarPositioner.GetTaskbarInfo(s.MonitorIndex);
                s.Position = "Custom";
                s.CustomX = Math.Max(0, (x - (tb?.Left ?? 0)) / scale);
            }
            _settings.NotifyChanged();
            return;
        }

        // Clic simple: abre el panel (con retardo corto para no chocar con el doble clic)
        if (_settings.Settings.ClickOpensPanel)
        {
            _clickTimer.Stop();
            _clickTimer.Start();
        }
    }

    private void DoDoubleClickAction()
    {
        switch (_settings.Settings.DoubleClickAction)
        {
            case "PlayPause":
                _media.TogglePlayPause();
                break;
            case "OpenSettings":
                ((App)Application.Current).OpenSettings();
                break;
            case "OpenApp":
                if (_currentKind == SourceKind.Spotify) AppActivator.OpenSpotify();
                else if (_currentKind == SourceKind.Browser) AppActivator.ActivateBrowser();
                break;
        }
    }

    // ----- Controles y menú -----

    private void OnPlayPause(object sender, RoutedEventArgs e) => _media.TogglePlayPause();
    private void OnPrev(object sender, RoutedEventArgs e) => _media.SkipPrevious();
    private void OnNext(object sender, RoutedEventArgs e) => _media.SkipNext();

    private void OnMouseEnterWindow(object sender, EventArgs e) => FadeControls(1);
    private void OnMouseLeaveWindow(object sender, EventArgs e) => FadeControls(0);

    private void FadeControls(double to)
    {
        if (!_settings.Settings.ShowControls) return;
        ControlsPanel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(150)));
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // Sin esto, con WS_EX_NOACTIVATE el menú no captura el ratón:
        // no responde bien y no se cierra al hacer clic fuera.
        AppActivator.FocusWindow(this);
    }

    private void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        var s = _settings.Settings;
        SourceAutoItem.IsChecked = s.PreferredSource == "Auto";
        SourceSpotifyItem.IsChecked = s.PreferredSource == "Spotify";
        SourceBrowserItem.IsChecked = s.PreferredSource == "Browser";
        FreeModeItem.IsChecked = s.Position == "Free";
        BuildHistoryMenu();
    }

    private void BuildHistoryMenu()
    {
        HistoryMenu.Items.Clear();
        var items = _history.Items;

        if (items.Count == 0)
        {
            HistoryMenu.Items.Add(new MenuItem { Header = "(vacío)", IsEnabled = false });
            return;
        }

        foreach (var entry in items.Take(15))
        {
            string text = string.IsNullOrWhiteSpace(entry.Artist)
                ? entry.Title
                : $"{entry.Artist} — {entry.Title}";
            var item = new MenuItem
            {
                Header = text.Replace("_", "__"), // los _ serían aceleradores de menú
                ToolTip = $"{entry.When:g} · {entry.Source}\nClic para copiar",
            };
            item.Click += (_, _) => { try { Clipboard.SetText(text); } catch { } };
            HistoryMenu.Items.Add(item);
        }

        HistoryMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Borrar historial" };
        clear.Click += (_, _) => _history.Clear();
        HistoryMenu.Items.Add(clear);
    }

    private void OnToggleFreeMode(object sender, RoutedEventArgs e)
    {
        var s = _settings.Settings;
        s.Position = s.Position == "Free" ? "Right" : "Free";
        _settings.NotifyChanged();
    }

    private void OnSourceAuto(object sender, RoutedEventArgs e) => SetSource("Auto");
    private void OnSourceSpotify(object sender, RoutedEventArgs e) => SetSource("Spotify");
    private void OnSourceBrowser(object sender, RoutedEventArgs e) => SetSource("Browser");

    private void SetSource(string source)
    {
        _settings.Settings.PreferredSource = source;
        _settings.NotifyChanged();
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e) => ((App)Application.Current).OpenSettings();
    private void OnExit(object sender, RoutedEventArgs e) => ((App)Application.Current).ExitApp();
}
