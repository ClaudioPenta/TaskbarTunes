namespace TaskbarTunes.Models;

/// <summary>
/// Configuración persistida en %APPDATA%\TaskbarTunes\settings.json.
/// Los colores se guardan como cadenas hex #AARRGGBB.
/// </summary>
public class AppSettings
{
    // ----- Posición y tamaño -----
    public int WidgetWidth { get; set; } = 320;

    /// <summary>"Right" (junto al reloj), "Center", "Left", "Custom" (arrastrado) o "Free" (overlay flotante).</summary>
    public string Position { get; set; } = "Right";

    /// <summary>Desplazamiento horizontal extra en píxeles lógicos (modos Right/Center/Left).</summary>
    public int OffsetX { get; set; } = 8;

    /// <summary>Modo Custom: distancia en px lógicos desde el borde izquierdo de la barra.</summary>
    public double CustomX { get; set; } = -1;

    /// <summary>Modo Free (overlay): posición en pantalla en px lógicos.</summary>
    public double FreeX { get; set; } = -1;
    public double FreeY { get; set; } = -1;

    /// <summary>True cuando el usuario ya colocó el widget en modo Free (FreeX/Y son válidos).</summary>
    public bool FreePlaced { get; set; } = false;

    /// <summary>Alto del widget en modo Free (en la barra se usa el alto de la barra).</summary>
    public int FreeHeight { get; set; } = 48;

    public double CornerRadius { get; set; } = 10;

    /// <summary>Pantalla donde vive el widget en modo barra (índice de Screen.AllScreens).</summary>
    public int MonitorIndex { get; set; } = 0;

    // ----- Colores -----
    public string BackgroundColor { get; set; } = "#66151515";
    public string TextColor { get; set; } = "#FFFFFFFF";

    /// <summary>Desenfoque acrílico nativo de Windows detrás del widget.</summary>
    public bool AcrylicBackground { get; set; } = false;

    // ----- Visualizador -----
    public bool ShowVisualizer { get; set; } = true;

    /// <summary>"Bars", "MirrorBars", "Wave", "Dots", "Leds" o "FilledWave".</summary>
    public string VisualizerStyle { get; set; } = "Bars";

    public int VisualizerBarCount { get; set; } = 28;
    public double VisualizerOpacity { get; set; } = 0.55;
    public string VisualizerColor { get; set; } = "#FF1DB954";
    public string VisualizerColor2 { get; set; } = "#FF00C8FF";
    public bool VisualizerGradient { get; set; } = true;

    /// <summary>Separación entre barras en px (0–6).</summary>
    public double VisualizerBarGap { get; set; } = 2;

    /// <summary>"Vertical" u "Horizontal".</summary>
    public string GradientDirection { get; set; } = "Vertical";

    /// <summary>Modo «beat»: el visualizador reacciona solo a los graves (~50–200 Hz).</summary>
    public bool BeatMode { get; set; } = false;

    /// <summary>
    /// Qué audio escucha el visualizador: "App" (solo el proceso de la app de
    /// música — los juegos no lo mueven) o "System" (todo lo que suena).
    /// </summary>
    public string VisualizerAudioSource { get; set; } = "App";

    /// <summary>Tomar los colores del visualizador de la carátula de la canción.</summary>
    public bool AdaptiveColors { get; set; } = false;

    // ----- Contenido -----
    public bool ShowAlbumArt { get; set; } = true;

    /// <summary>"Square" o "Vinyl" (disco girando mientras suena).</summary>
    public string AlbumArtStyle { get; set; } = "Square";

    /// <summary>Transición suave al cambiar de carátula.</summary>
    public bool CrossfadeArt { get; set; } = true;

    /// <summary>Un clic en el widget abre el panel expandido.</summary>
    public bool ClickOpensPanel { get; set; } = true;
    public bool ShowControls { get; set; } = true;
    public bool CleanYouTubeTitles { get; set; } = true;
    public bool ShowProgress { get; set; } = true;
    public bool ShowArtist { get; set; } = true;
    public bool ShowSourceIcon { get; set; } = false;
    public bool HideWhenNoMusic { get; set; } = false;

    public string FontFamily { get; set; } = "Segoe UI";
    public double TitleFontSize { get; set; } = 12;

    // ----- Comportamiento -----
    /// <summary>"Auto", "Spotify" o "Browser".</summary>
    public string PreferredSource { get; set; } = "Auto";

    /// <summary>"None", "PlayPause", "OpenApp" u "OpenSettings".</summary>
    public string DoubleClickAction { get; set; } = "PlayPause";
}
