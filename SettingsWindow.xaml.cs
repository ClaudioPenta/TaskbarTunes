using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskbarTunes.Controls;
using TaskbarTunes.Services;
using WinForms = System.Windows.Forms;

namespace TaskbarTunes;

/// <summary>
/// Ventana de ajustes en pestañas: lee la configuración al abrir y aplica cada
/// cambio en vivo a través de <see cref="SettingsService.NotifyChanged"/>.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsService _svc;
    private readonly PresetService _presets = new();
    private bool _loading = true;

    public SettingsWindow(SettingsService svc)
    {
        InitializeComponent();
        _svc = svc;
        var s = svc.Settings;

        // ---- Apariencia ----
        WidthSlider.Value = s.WidgetWidth;
        CornerSlider.Value = s.CornerRadius;
        OffsetSlider.Value = s.OffsetX;
        FreeHeightSlider.Value = s.FreeHeight;
        PositionCombo.SelectedIndex = s.Position switch
        {
            "Center" => 1, "Left" => 2, "Custom" => 3, "Free" => 4, _ => 0,
        };
        BgColorPicker.SelectedColor = AudioVisualizer.ParseColor(s.BackgroundColor, Color.FromArgb(0x66, 0x15, 0x15, 0x15));
        TextColorPicker.SelectedColor = AudioVisualizer.ParseColor(s.TextColor, Colors.White);

        var fonts = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n).ToList();
        if (!fonts.Contains(s.FontFamily)) fonts.Insert(0, s.FontFamily);
        FontCombo.ItemsSource = fonts;
        FontCombo.SelectedItem = s.FontFamily;
        FontSizeSlider.Value = s.TitleFontSize;

        var screens = WinForms.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var b = screens[i].Bounds;
            string label = $"Pantalla {i + 1} ({b.Width}×{b.Height}){(screens[i].Primary ? " — principal" : "")}";
            MonitorCombo.Items.Add(new ComboBoxItem { Content = label });
        }
        MonitorCombo.SelectedIndex = Math.Clamp(s.MonitorIndex, 0, screens.Length - 1);
        AcrylicCheck.IsChecked = s.AcrylicBackground;

        // ---- Visualizador ----
        ShowVisCheck.IsChecked = s.ShowVisualizer;
        StyleCombo.SelectedIndex = s.VisualizerStyle switch
        {
            "MirrorBars" => 1, "Wave" => 2, "FilledWave" => 3, "Dots" => 4, "Leds" => 5, _ => 0,
        };
        BarsSlider.Value = s.VisualizerBarCount;
        GapSlider.Value = s.VisualizerBarGap;
        OpacitySlider.Value = s.VisualizerOpacity;
        VisColor1Picker.SelectedColor = AudioVisualizer.ParseColor(s.VisualizerColor, Colors.LimeGreen);
        VisColor2Picker.SelectedColor = AudioVisualizer.ParseColor(s.VisualizerColor2, Colors.DeepSkyBlue);
        GradientDirCombo.SelectedIndex = s.GradientDirection == "Horizontal" ? 1 : 0;
        AudioSourceCombo.SelectedIndex = s.VisualizerAudioSource == "System" ? 1 : 0;
        GradientCheck.IsChecked = s.VisualizerGradient;
        AdaptiveCheck.IsChecked = s.AdaptiveColors;
        BeatCheck.IsChecked = s.BeatMode;

        // ---- Contenido ----
        ShowArtCheck.IsChecked = s.ShowAlbumArt;
        VinylCheck.IsChecked = s.AlbumArtStyle == "Vinyl";
        CrossfadeCheck.IsChecked = s.CrossfadeArt;
        ClickPanelCheck.IsChecked = s.ClickOpensPanel;
        ShowArtistCheck.IsChecked = s.ShowArtist;
        ShowControlsCheck.IsChecked = s.ShowControls;
        ShowProgressCheck.IsChecked = s.ShowProgress;
        ShowSourceIconCheck.IsChecked = s.ShowSourceIcon;
        CleanTitlesCheck.IsChecked = s.CleanYouTubeTitles;
        HideNoMusicCheck.IsChecked = s.HideWhenNoMusic;

        // ---- Sistema ----
        SourceCombo.SelectedIndex = s.PreferredSource switch { "Spotify" => 1, "Browser" => 2, _ => 0 };
        DoubleClickCombo.SelectedIndex = s.DoubleClickAction switch
        {
            "None" => 0, "OpenApp" => 2, "OpenSettings" => 3, _ => 1,
        };
        AutoStartCheck.IsChecked = App.IsAutoStartEnabled();

        // ---- Temas ----
        BuildThemeButtons();
        RefreshPresetList();

        UpdateLabels();
        _loading = false;
        WireEvents();
    }

    private void WireEvents()
    {
        WidthSlider.ValueChanged += (_, _) => Apply();
        CornerSlider.ValueChanged += (_, _) => Apply();
        OffsetSlider.ValueChanged += (_, _) => Apply();
        FreeHeightSlider.ValueChanged += (_, _) => Apply();
        PositionCombo.SelectionChanged += (_, _) => Apply();
        BgColorPicker.ColorChanged += _ => Apply();
        TextColorPicker.ColorChanged += _ => Apply();
        FontCombo.SelectionChanged += (_, _) => Apply();
        FontSizeSlider.ValueChanged += (_, _) => Apply();
        MonitorCombo.SelectionChanged += (_, _) => Apply();
        AcrylicCheck.Checked += Apply; AcrylicCheck.Unchecked += Apply;

        ShowVisCheck.Checked += Apply; ShowVisCheck.Unchecked += Apply;
        StyleCombo.SelectionChanged += (_, _) => Apply();
        BarsSlider.ValueChanged += (_, _) => Apply();
        GapSlider.ValueChanged += (_, _) => Apply();
        OpacitySlider.ValueChanged += (_, _) => Apply();
        VisColor1Picker.ColorChanged += _ => Apply();
        VisColor2Picker.ColorChanged += _ => Apply();
        GradientDirCombo.SelectionChanged += (_, _) => Apply();
        AudioSourceCombo.SelectionChanged += (_, _) => Apply();
        GradientCheck.Checked += Apply; GradientCheck.Unchecked += Apply;
        AdaptiveCheck.Checked += Apply; AdaptiveCheck.Unchecked += Apply;
        BeatCheck.Checked += Apply; BeatCheck.Unchecked += Apply;

        ShowArtCheck.Checked += Apply; ShowArtCheck.Unchecked += Apply;
        VinylCheck.Checked += Apply; VinylCheck.Unchecked += Apply;
        CrossfadeCheck.Checked += Apply; CrossfadeCheck.Unchecked += Apply;
        ClickPanelCheck.Checked += Apply; ClickPanelCheck.Unchecked += Apply;
        ShowArtistCheck.Checked += Apply; ShowArtistCheck.Unchecked += Apply;
        ShowControlsCheck.Checked += Apply; ShowControlsCheck.Unchecked += Apply;
        ShowProgressCheck.Checked += Apply; ShowProgressCheck.Unchecked += Apply;
        ShowSourceIconCheck.Checked += Apply; ShowSourceIconCheck.Unchecked += Apply;
        CleanTitlesCheck.Checked += Apply; CleanTitlesCheck.Unchecked += Apply;
        HideNoMusicCheck.Checked += Apply; HideNoMusicCheck.Unchecked += Apply;

        SourceCombo.SelectionChanged += (_, _) => Apply();
        DoubleClickCombo.SelectionChanged += (_, _) => Apply();
        AutoStartCheck.Checked += (_, _) => App.SetAutoStart(true);
        AutoStartCheck.Unchecked += (_, _) => App.SetAutoStart(false);
    }

    private void Apply(object? sender = null, RoutedEventArgs? e = null)
    {
        if (_loading) return;
        var s = _svc.Settings;

        s.WidgetWidth = (int)WidthSlider.Value;
        s.CornerRadius = (int)CornerSlider.Value;
        s.OffsetX = (int)OffsetSlider.Value;
        s.FreeHeight = (int)FreeHeightSlider.Value;
        s.Position = PositionCombo.SelectedIndex switch
        {
            1 => "Center", 2 => "Left", 3 => "Custom", 4 => "Free", _ => "Right",
        };
        s.BackgroundColor = BgColorPicker.SelectedColor.ToString();
        s.TextColor = TextColorPicker.SelectedColor.ToString();
        s.FontFamily = FontCombo.SelectedItem as string ?? "Segoe UI";
        s.TitleFontSize = Math.Round(FontSizeSlider.Value, 1);
        s.MonitorIndex = Math.Max(0, MonitorCombo.SelectedIndex);
        s.AcrylicBackground = AcrylicCheck.IsChecked == true;

        s.ShowVisualizer = ShowVisCheck.IsChecked == true;
        s.VisualizerStyle = StyleCombo.SelectedIndex switch
        {
            1 => "MirrorBars", 2 => "Wave", 3 => "FilledWave", 4 => "Dots", 5 => "Leds", _ => "Bars",
        };
        s.VisualizerBarCount = (int)BarsSlider.Value;
        s.VisualizerBarGap = Math.Round(GapSlider.Value, 1);
        s.VisualizerOpacity = Math.Round(OpacitySlider.Value, 2);
        s.VisualizerColor = VisColor1Picker.SelectedColor.ToString();
        s.VisualizerColor2 = VisColor2Picker.SelectedColor.ToString();
        s.GradientDirection = GradientDirCombo.SelectedIndex == 1 ? "Horizontal" : "Vertical";
        s.VisualizerAudioSource = AudioSourceCombo.SelectedIndex == 1 ? "System" : "App";
        s.VisualizerGradient = GradientCheck.IsChecked == true;
        s.AdaptiveColors = AdaptiveCheck.IsChecked == true;
        s.BeatMode = BeatCheck.IsChecked == true;

        s.ShowAlbumArt = ShowArtCheck.IsChecked == true;
        s.AlbumArtStyle = VinylCheck.IsChecked == true ? "Vinyl" : "Square";
        s.CrossfadeArt = CrossfadeCheck.IsChecked == true;
        s.ClickOpensPanel = ClickPanelCheck.IsChecked == true;
        s.ShowArtist = ShowArtistCheck.IsChecked == true;
        s.ShowControls = ShowControlsCheck.IsChecked == true;
        s.ShowProgress = ShowProgressCheck.IsChecked == true;
        s.ShowSourceIcon = ShowSourceIconCheck.IsChecked == true;
        s.CleanYouTubeTitles = CleanTitlesCheck.IsChecked == true;
        s.HideWhenNoMusic = HideNoMusicCheck.IsChecked == true;

        s.PreferredSource = SourceCombo.SelectedIndex switch { 1 => "Spotify", 2 => "Browser", _ => "Auto" };
        s.DoubleClickAction = DoubleClickCombo.SelectedIndex switch
        {
            0 => "None", 2 => "OpenApp", 3 => "OpenSettings", _ => "PlayPause",
        };

        UpdateLabels();
        _svc.NotifyChanged();
    }

    private void UpdateLabels()
    {
        WidthValue.Text = $"{(int)WidthSlider.Value} px";
        CornerValue.Text = $"{(int)CornerSlider.Value}";
        OffsetValue.Text = $"{(int)OffsetSlider.Value}";
        FreeHeightValue.Text = $"{(int)FreeHeightSlider.Value} px";
        FontSizeValue.Text = $"{FontSizeSlider.Value:0.#}";
        BarsValue.Text = $"{(int)BarsSlider.Value}";
        GapValue.Text = $"{GapSlider.Value:0.#} px";
        OpacityValue.Text = $"{OpacitySlider.Value:P0}";
    }

    // ----- Temas y presets -----

    private void BuildThemeButtons()
    {
        foreach (var (name, _) in PresetService.BuiltIn)
        {
            var btn = new Button
            {
                Content = name,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(12, 5, 12, 5),
            };
            btn.Click += (_, _) =>
            {
                PresetService.ApplyBuiltIn(name, _svc.Settings);
                ReloadFromSettings();
                _svc.NotifyChanged();
            };
            ThemesPanel.Children.Add(btn);
        }
    }

    private void RefreshPresetList()
    {
        PresetList.ItemsSource = null;
        PresetList.ItemsSource = _presets.ListCustom();
    }

    private void OnApplyPreset(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not string name) return;
        if (_presets.ApplyCustom(name, _svc.Settings))
        {
            ReloadFromSettings();
            _svc.NotifyChanged();
        }
    }

    private void OnDeletePreset(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not string name) return;
        if (MessageBox.Show(this, $"¿Eliminar el preset «{name}»?", "TaskbarTunes",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _presets.DeleteCustom(name);
            RefreshPresetList();
        }
    }

    private void OnSavePreset(object sender, RoutedEventArgs e)
    {
        string name = PresetNameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Escribe un nombre para el preset.", "TaskbarTunes",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _presets.SaveCustom(name, _svc.Settings);
        PresetNameBox.Clear();
        RefreshPresetList();
    }

    /// <summary>Tras aplicar un tema/preset, refleja los valores nuevos en los controles.</summary>
    private void ReloadFromSettings()
    {
        var s = _svc.Settings;
        _loading = true;

        CornerSlider.Value = s.CornerRadius;
        BgColorPicker.SelectedColor = AudioVisualizer.ParseColor(s.BackgroundColor, Colors.Black);
        TextColorPicker.SelectedColor = AudioVisualizer.ParseColor(s.TextColor, Colors.White);
        if (FontCombo.ItemsSource is List<string> fonts && !fonts.Contains(s.FontFamily))
        {
            fonts.Insert(0, s.FontFamily);
            FontCombo.ItemsSource = null;
            FontCombo.ItemsSource = fonts;
        }
        FontCombo.SelectedItem = s.FontFamily;
        FontSizeSlider.Value = s.TitleFontSize;

        ShowVisCheck.IsChecked = s.ShowVisualizer;
        StyleCombo.SelectedIndex = s.VisualizerStyle switch
        {
            "MirrorBars" => 1, "Wave" => 2, "FilledWave" => 3, "Dots" => 4, "Leds" => 5, _ => 0,
        };
        BarsSlider.Value = s.VisualizerBarCount;
        GapSlider.Value = s.VisualizerBarGap;
        OpacitySlider.Value = s.VisualizerOpacity;
        VisColor1Picker.SelectedColor = AudioVisualizer.ParseColor(s.VisualizerColor, Colors.LimeGreen);
        VisColor2Picker.SelectedColor = AudioVisualizer.ParseColor(s.VisualizerColor2, Colors.DeepSkyBlue);
        GradientDirCombo.SelectedIndex = s.GradientDirection == "Horizontal" ? 1 : 0;
        GradientCheck.IsChecked = s.VisualizerGradient;
        AdaptiveCheck.IsChecked = s.AdaptiveColors;
        AcrylicCheck.IsChecked = s.AcrylicBackground;
        VinylCheck.IsChecked = s.AlbumArtStyle == "Vinyl";
        CrossfadeCheck.IsChecked = s.CrossfadeArt;

        UpdateLabels();
        _loading = false;
    }
}
