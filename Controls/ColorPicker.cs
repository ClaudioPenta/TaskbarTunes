using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TaskbarTunes.Helpers;

namespace TaskbarTunes.Controls;

/// <summary>
/// Selector de color compacto: muestra swatch + caja hex; al pulsar el swatch
/// se abre un popup con cuadro saturación/brillo, barra de tono y barra de
/// transparencia. Dispara <see cref="ColorChanged"/> en cada movimiento para
/// que los ajustes se vean en vivo.
/// </summary>
public class ColorPicker : ContentControl
{
    private const double SvW = 178, SvH = 140, HueW = 16, AlphaH = 14;

    private double _h, _s = 1, _v = 1;
    private byte _a = 255;
    private bool _updating;

    private readonly Border _swatchFill;
    private readonly TextBox _hexBox;
    private readonly Popup _popup;
    private readonly Canvas _svCanvas;
    private readonly Rectangle _svBase;
    private readonly Ellipse _svThumb;
    private readonly Canvas _hueCanvas;
    private readonly Border _hueThumb;
    private readonly Canvas _alphaCanvas;
    private readonly Rectangle _alphaGradient;
    private readonly Border _alphaThumb;

    public event Action<Color>? ColorChanged;

    public Color SelectedColor
    {
        get => ColorExtractor.HsvToColor(_h, _s, _v, _a);
        set
        {
            _a = value.A;
            (_h, _s, _v) = ColorExtractor.RgbToHsv(value.R / 255.0, value.G / 255.0, value.B / 255.0);
            RefreshUi();
        }
    }

    public ColorPicker()
    {
        Focusable = false;

        // ---- Swatch + hex ----
        var swatchOuter = new Border
        {
            Width = 36, Height = 20, CornerRadius = new CornerRadius(3),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Background = CreateCheckerBrush(),
            Cursor = Cursors.Hand,
        };
        _swatchFill = new Border { CornerRadius = new CornerRadius(2) };
        swatchOuter.Child = _swatchFill;
        swatchOuter.MouseLeftButtonDown += (_, e) => { _popup!.IsOpen = true; e.Handled = true; };

        _hexBox = new TextBox { Width = 88, Margin = new Thickness(6, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
        _hexBox.TextChanged += OnHexChanged;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(swatchOuter);
        row.Children.Add(_hexBox);

        // ---- Popup: cuadro SV ----
        _svBase = new Rectangle { Width = SvW, Height = SvH };
        var whiteOverlay = new Rectangle
        {
            Width = SvW, Height = SvH,
            Fill = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0),
        };
        var blackOverlay = new Rectangle
        {
            Width = SvW, Height = SvH,
            Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90),
        };
        _svThumb = new Ellipse
        {
            Width = 12, Height = 12,
            Stroke = Brushes.White, StrokeThickness = 2,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0 },
            IsHitTestVisible = false,
        };
        _svCanvas = new Canvas { Width = SvW, Height = SvH, Background = Brushes.Transparent, Cursor = Cursors.Cross };
        _svCanvas.Children.Add(_svBase);
        _svCanvas.Children.Add(whiteOverlay);
        _svCanvas.Children.Add(blackOverlay);
        _svCanvas.Children.Add(_svThumb);
        HookDrag(_svCanvas, p =>
        {
            _s = Math.Clamp(p.X / SvW, 0, 1);
            _v = Math.Clamp(1 - p.Y / SvH, 0, 1);
        });

        // ---- Popup: barra de tono ----
        var hueRect = new Rectangle { Width = HueW, Height = SvH, Fill = CreateHueBrush() };
        _hueThumb = new Border
        {
            Width = HueW, Height = 4, Background = Brushes.Transparent,
            BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(2), IsHitTestVisible = false,
        };
        _hueCanvas = new Canvas { Width = HueW, Height = SvH, Margin = new Thickness(10, 0, 0, 0), Cursor = Cursors.Hand };
        _hueCanvas.Children.Add(hueRect);
        _hueCanvas.Children.Add(_hueThumb);
        HookDrag(_hueCanvas, p => _h = Math.Clamp(p.Y / SvH, 0, 0.9999) * 360);

        // ---- Popup: barra de transparencia ----
        double alphaW = SvW + HueW + 10;
        var alphaChecker = new Rectangle { Width = alphaW, Height = AlphaH, Fill = CreateCheckerBrush(), RadiusX = 3, RadiusY = 3 };
        _alphaGradient = new Rectangle { Width = alphaW, Height = AlphaH, RadiusX = 3, RadiusY = 3 };
        _alphaThumb = new Border
        {
            Width = 6, Height = AlphaH, Background = Brushes.Transparent,
            BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(2), IsHitTestVisible = false,
        };
        _alphaCanvas = new Canvas { Width = alphaW, Height = AlphaH, Margin = new Thickness(0, 10, 0, 0), Cursor = Cursors.Hand };
        _alphaCanvas.Children.Add(alphaChecker);
        _alphaCanvas.Children.Add(_alphaGradient);
        _alphaCanvas.Children.Add(_alphaThumb);
        HookDrag(_alphaCanvas, p => _a = (byte)Math.Clamp(p.X / alphaW * 255, 0, 255));

        var pickerArea = new StackPanel { Orientation = Orientation.Horizontal };
        pickerArea.Children.Add(_svCanvas);
        pickerArea.Children.Add(_hueCanvas);

        var popupRoot = new StackPanel { Margin = new Thickness(10) };
        popupRoot.Children.Add(pickerArea);
        popupRoot.Children.Add(_alphaCanvas);

        _popup = new Popup
        {
            PlacementTarget = swatchOuter,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = SystemColors.WindowBrush,
                BorderBrush = SystemColors.ActiveBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.3 },
                Child = popupRoot,
            },
        };

        var root = new Grid();
        root.Children.Add(row);
        root.Children.Add(_popup);
        Content = root;

        RefreshUi();
    }

    /// <summary>Arrastre continuo sobre un canvas: aplica el punto y notifica en vivo.</summary>
    private void HookDrag(Canvas canvas, Action<Point> apply)
    {
        void Handle(MouseEventArgs e)
        {
            apply(e.GetPosition(canvas));
            RefreshUi();
            Notify();
        }
        canvas.MouseLeftButtonDown += (_, e) => { canvas.CaptureMouse(); Handle(e); e.Handled = true; };
        canvas.MouseMove += (_, e) => { if (canvas.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Handle(e); };
        canvas.MouseLeftButtonUp += (_, _) => canvas.ReleaseMouseCapture();
    }

    private void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(_hexBox.Text.Trim());
            _a = c.A;
            (_h, _s, _v) = ColorExtractor.RgbToHsv(c.R / 255.0, c.G / 255.0, c.B / 255.0);
            RefreshUi(updateHex: false);
            Notify();
        }
        catch { /* hex a medio escribir */ }
    }

    private void Notify() => ColorChanged?.Invoke(SelectedColor);

    private void RefreshUi(bool updateHex = true)
    {
        _updating = true;

        var color = SelectedColor;
        var opaqueHue = ColorExtractor.HsvToColor(_h, 1, 1);
        var opaqueColor = ColorExtractor.HsvToColor(_h, _s, _v);

        _swatchFill.Background = new SolidColorBrush(color);
        if (updateHex) _hexBox.Text = color.ToString();

        _svBase.Fill = new SolidColorBrush(opaqueHue);
        Canvas.SetLeft(_svThumb, _s * SvW - 6);
        Canvas.SetTop(_svThumb, (1 - _v) * SvH - 6);

        Canvas.SetTop(_hueThumb, _h / 360 * SvH - 2);

        _alphaGradient.Fill = new LinearGradientBrush(
            Color.FromArgb(0, opaqueColor.R, opaqueColor.G, opaqueColor.B), opaqueColor, 0);
        Canvas.SetLeft(_alphaThumb, _a / 255.0 * (_alphaCanvas.Width - 6));

        _updating = false;
    }

    private static Brush CreateHueBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        for (int i = 0; i <= 6; i++)
            brush.GradientStops.Add(new GradientStop(ColorExtractor.HsvToColor(i * 60 % 360, 1, 1), i / 6.0));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateCheckerBrush()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        var gray = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        group.Children.Add(new GeometryDrawing(gray, null, new RectangleGeometry(new Rect(0, 0, 4, 4))));
        group.Children.Add(new GeometryDrawing(gray, null, new RectangleGeometry(new Rect(4, 4, 4, 4))));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }
}
