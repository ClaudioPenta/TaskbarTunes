using System.Windows;
using System.Windows.Media;
using TaskbarTunes.Models;
using TaskbarTunes.Services;

namespace TaskbarTunes.Controls;

/// <summary>
/// Dibuja el visualizador (barras, barras espejadas, onda, puntos, LEDs u onda
/// rellena) leyendo los niveles de <see cref="AudioCaptureService"/> en cada
/// frame, con ataque rápido y caída suave. En modo «beat» solo usa las bandas
/// graves (~50–200 Hz). Si hay colores adaptativos (de la carátula), sustituyen
/// a los configurados.
/// </summary>
public class AudioVisualizer : FrameworkElement
{
    private const int BeatBands = 16; // primeras 16 de 64 bandas ≈ 50–200 Hz

    private AudioCaptureService? _source;
    private readonly float[] _targets = new float[AudioCaptureService.BandCount];
    private readonly float[] _display = new float[AudioCaptureService.BandCount];

    private string _style = "Bars";
    private int _barCount = 28;
    private double _barGap = 2;
    private bool _beatMode;
    private bool _horizontalGradient;
    private bool _useGradient = true;
    private Color _color1 = Colors.LimeGreen;
    private Color _color2 = Colors.DeepSkyBlue;
    private Color? _adaptive1, _adaptive2;
    private bool _adaptiveEnabled;

    private Brush _brush = Brushes.LimeGreen;
    private Pen? _wavePen;
    private bool _rendering;
    private bool _wasActive;
    private long _lastFrameTicks;

    public AudioVisualizer()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => SetRendering(true);
        Unloaded += (_, _) => SetRendering(false);
    }

    public void Attach(AudioCaptureService source) => _source = source;

    public void ApplySettings(AppSettings s)
    {
        _style = s.VisualizerStyle;
        _barCount = Math.Clamp(s.VisualizerBarCount, 4, AudioCaptureService.BandCount);
        _barGap = Math.Clamp(s.VisualizerBarGap, 0, 8);
        _beatMode = s.BeatMode;
        _horizontalGradient = s.GradientDirection == "Horizontal";
        _useGradient = s.VisualizerGradient;
        _color1 = ParseColor(s.VisualizerColor, Colors.LimeGreen);
        _color2 = ParseColor(s.VisualizerColor2, Colors.DeepSkyBlue);
        _adaptiveEnabled = s.AdaptiveColors;

        Opacity = s.VisualizerOpacity;
        Visibility = s.ShowVisualizer ? Visibility.Visible : Visibility.Collapsed;

        RebuildBrush();
        InvalidateVisual();
    }

    /// <summary>Colores extraídos de la carátula (null para volver a los configurados).</summary>
    public void SetAdaptiveColors(Color? main, Color? secondary)
    {
        _adaptive1 = main;
        _adaptive2 = secondary;
        RebuildBrush();
        InvalidateVisual();
    }

    private void RebuildBrush()
    {
        Color c1 = _adaptiveEnabled && _adaptive1.HasValue ? _adaptive1.Value : _color1;
        Color c2 = _adaptiveEnabled && _adaptive2.HasValue ? _adaptive2.Value : _color2;

        if (_useGradient || (_adaptiveEnabled && _adaptive1.HasValue))
        {
            // vertical: c2 arriba → c1 abajo; horizontal: c1 izquierda → c2 derecha
            _brush = _horizontalGradient
                ? new LinearGradientBrush(c1, c2, 0)
                : new LinearGradientBrush(c2, c1, 90);
        }
        else
        {
            _brush = new SolidColorBrush(c1);
        }
        _brush.Freeze();

        var penBrush = new SolidColorBrush(c2) { Opacity = 0.9 };
        penBrush.Freeze();
        _wavePen = new Pen(penBrush, 1.4);
        _wavePen.Freeze();
    }

    public static Color ParseColor(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }

    private void SetRendering(bool on)
    {
        if (on == _rendering) return;
        _rendering = on;
        if (on) CompositionTarget.Rendering += OnFrame;
        else CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;
        if (now - _lastFrameTicks < 28) return; // ~33 fps
        _lastFrameTicks = now;

        if (Visibility != Visibility.Visible) return;

        _source?.ReadBands(_targets);

        bool anyActivity = false;
        for (int i = 0; i < _display.Length; i++)
        {
            float target = _targets[i];
            float current = _display[i];
            // Ataque rápido, caída suave
            _display[i] = target > current ? current + (target - current) * 0.55f : current * 0.86f;
            if (_display[i] > 0.004f) anyActivity = true;
            else _display[i] = 0;
        }

        if (anyActivity || _wasActive)
            InvalidateVisual();
        _wasActive = anyActivity;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4) return;

        int n = _barCount;
        Span<float> levels = stackalloc float[n];

        // En modo beat solo se reparten las bandas graves entre las barras
        int available = _beatMode ? BeatBands : _display.Length;
        for (int b = 0; b < n; b++)
        {
            int i0 = b * available / n;
            int i1 = Math.Max(i0 + 1, (b + 1) * available / n);
            float sum = 0;
            for (int i = i0; i < i1; i++) sum += _display[i];
            levels[b] = sum / (i1 - i0);
        }

        switch (_style)
        {
            case "Wave": RenderWave(dc, levels, w, h, filled: false); break;
            case "FilledWave": RenderWave(dc, levels, w, h, filled: true); break;
            case "MirrorBars": RenderBars(dc, levels, w, h, mirror: true); break;
            case "Dots": RenderDots(dc, levels, w, h); break;
            case "Leds": RenderLeds(dc, levels, w, h); break;
            default: RenderBars(dc, levels, w, h, mirror: false); break;
        }
    }

    private double BarWidth(int n, double w, out double gap)
    {
        gap = _barGap;
        double barW = (w - gap * (n - 1)) / n;
        if (barW < 1) { gap = Math.Max(0, gap - 1); barW = Math.Max(1, (w - gap * (n - 1)) / n); }
        return barW;
    }

    private void RenderBars(DrawingContext dc, Span<float> levels, double w, double h, bool mirror)
    {
        int n = levels.Length;
        double barW = BarWidth(n, w, out double gap);
        double radius = Math.Min(barW / 2, 2);

        for (int i = 0; i < n; i++)
        {
            double level = levels[i];
            if (level <= 0.004) continue;
            double x = i * (barW + gap);

            if (mirror)
            {
                double half = h / 2;
                double bh = Math.Max(1, level * (half - 1));
                dc.DrawRoundedRectangle(_brush, null,
                    new Rect(x, half - bh, barW, bh * 2), radius, radius);
            }
            else
            {
                double bh = Math.Max(1, level * (h - 2));
                dc.DrawRoundedRectangle(_brush, null,
                    new Rect(x, h - bh, barW, bh), radius, radius);
            }
        }
    }

    private void RenderDots(DrawingContext dc, Span<float> levels, double w, double h)
    {
        int n = levels.Length;
        double barW = BarWidth(n, w, out double gap);
        double r = Math.Clamp(barW / 2, 1.5, 4);

        for (int i = 0; i < n; i++)
        {
            double level = levels[i];
            if (level <= 0.004) continue;
            double cx = i * (barW + gap) + barW / 2;
            double cy = h - r - level * (h - r * 2 - 2);
            dc.DrawEllipse(_brush, null, new Point(cx, cy), r, r);
            // Estela tenue debajo del punto
            if (level > 0.1)
            {
                dc.PushOpacity(0.25);
                dc.DrawRoundedRectangle(_brush, null,
                    new Rect(cx - r * 0.5, cy + r, r, Math.Max(0, h - cy - r * 2)), r * 0.5, r * 0.5);
                dc.Pop();
            }
        }
    }

    private void RenderLeds(DrawingContext dc, Span<float> levels, double w, double h)
    {
        int n = levels.Length;
        double barW = BarWidth(n, w, out double gap);
        const double segGap = 1.5;
        double segH = 3;
        int segCount = Math.Max(3, (int)((h - 2) / (segH + segGap)));

        for (int i = 0; i < n; i++)
        {
            int lit = (int)Math.Round(levels[i] * segCount);
            for (int s = 0; s < lit; s++)
            {
                double y = h - (s + 1) * (segH + segGap);
                if (y < 0) break;
                dc.DrawRoundedRectangle(_brush, null, new Rect(i * (barW + gap), y, barW, segH), 1, 1);
            }
        }
    }

    private void RenderWave(DrawingContext dc, Span<float> levels, double w, double h, bool filled)
    {
        int n = levels.Length;
        double mid = h / 2;
        double amp = h / 2 - 1;
        double step = w / (n - 1);

        var top = new Point[n];
        for (int i = 0; i < n; i++)
            top[i] = new Point(i * step, mid - levels[i] * amp);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(top[0], isFilled: true, isClosed: true);
            for (int i = 1; i < n; i++) ctx.LineTo(top[i], true, true);
            for (int i = n - 1; i >= 0; i--) // espejo inferior
                ctx.LineTo(new Point(top[i].X, mid + (mid - top[i].Y)), true, true);
        }
        geo.Freeze();
        dc.DrawGeometry(_brush, filled ? _wavePen : null, geo);
    }
}
