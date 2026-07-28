using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TaskbarTunes.Models;
using WinForms = System.Windows.Forms;

namespace TaskbarTunes.Services;

/// <summary>
/// Ancla la ventana del widget sobre la barra de tareas usando Win32:
/// obtiene el rectángulo de la barra (principal o secundaria en multi-monitor)
/// y de la bandeja del sistema, coloca el widget dentro y lo mantiene por
/// encima (la barra también es topmost y "gana" tras ciertos eventos, así que
/// se re-afirma periódicamente). También gestiona el modo Free (overlay).
/// </summary>
public static class TaskbarPositioner
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
    [DllImport("user32.dll")] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int newStyle);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);

    public sealed class TaskbarInfo
    {
        public int Left, Top, Right, Bottom;
        public int TrayLeft;          // borde izquierdo del área de la bandeja/reloj
        public bool IsVisible;        // false si la barra está auto-oculta ahora mismo
        public int Height => Bottom - Top;
        public int Width => Right - Left;
    }

    /// <summary>Marca la ventana como tool-window (fuera de Alt+Tab); opcionalmente sin activación.</summary>
    public static void MakeToolWindow(Window window, bool noActivate = true)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW;
        if (noActivate) ex |= WS_EX_NOACTIVATE;
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
    }

    /// <summary>Pantalla elegida (o la principal si el índice ya no existe).</summary>
    private static WinForms.Screen GetScreen(int monitorIndex)
    {
        var screens = WinForms.Screen.AllScreens;
        return monitorIndex >= 0 && monitorIndex < screens.Length
            ? screens[monitorIndex]
            : WinForms.Screen.PrimaryScreen ?? screens[0];
    }

    public static (int Left, int Top, int Right, int Bottom) GetScreenBounds(int monitorIndex)
    {
        var b = GetScreen(monitorIndex).Bounds;
        return (b.Left, b.Top, b.Right, b.Bottom);
    }

    public static (int Left, int Top, int Right, int Bottom) GetVirtualScreenBounds()
    {
        var b = WinForms.SystemInformation.VirtualScreen;
        return (b.Left, b.Top, b.Right, b.Bottom);
    }

    /// <summary>
    /// Barra de tareas de la pantalla indicada: la principal ("Shell_TrayWnd")
    /// o una secundaria ("Shell_SecondaryTrayWnd") cuyo rect caiga en esa pantalla.
    /// </summary>
    public static TaskbarInfo? GetTaskbarInfo(int monitorIndex = 0)
    {
        var screen = GetScreen(monitorIndex);
        var sb = screen.Bounds;

        var candidates = new List<IntPtr>();
        var primary = FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero) candidates.Add(primary);

        IntPtr sec = IntPtr.Zero;
        while ((sec = FindWindowEx(IntPtr.Zero, sec, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            candidates.Add(sec);

        IntPtr taskbar = IntPtr.Zero;
        RECT rect = default;
        foreach (var hwnd in candidates)
        {
            if (!GetWindowRect(hwnd, out var r)) continue;
            int cx = (r.Left + r.Right) / 2;
            int cy = (r.Top + r.Bottom) / 2;
            if (cx >= sb.Left && cx < sb.Right && cy >= sb.Top && cy < sb.Bottom)
            {
                taskbar = hwnd;
                rect = r;
                break;
            }
        }

        // Fallback: la barra principal aunque no esté en esa pantalla
        if (taskbar == IntPtr.Zero)
        {
            if (primary == IntPtr.Zero || !GetWindowRect(primary, out rect)) return null;
            taskbar = primary;
        }

        var info = new TaskbarInfo
        {
            Left = rect.Left,
            Top = rect.Top,
            Right = rect.Right,
            Bottom = rect.Bottom,
            TrayLeft = rect.Right - 200, // fallback si no se encuentra la bandeja
        };

        // La bandeja (TrayNotifyWnd) solo existe en la barra principal
        if (taskbar == primary)
        {
            var tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (tray != IntPtr.Zero && GetWindowRect(tray, out var trayRect) && trayRect.Left > rect.Left)
                info.TrayLeft = trayRect.Left;
        }

        // Barra auto-oculta: cuando se esconde queda pegada al borde inferior
        // de su pantalla (asumimos barra abajo, lo único oficial en Win11)
        info.IsVisible = IsWindowVisible(taskbar) && rect.Top < sb.Bottom - 4;

        return info;
    }

    /// <summary>
    /// Coloca el widget según la configuración. Devuelve false si el widget
    /// debe ocultarse (barra auto-oculta y no está en modo Free).
    /// </summary>
    public static bool Position(Window window, AppSettings settings)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return true;

        double scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        int widthPx = (int)Math.Round(settings.WidgetWidth * scale);

        // Modo Free: overlay flotante en cualquier punto (clamp al escritorio
        // virtual completo, para poder llevarlo a cualquier pantalla)
        if (settings.Position == "Free")
        {
            int heightF = (int)Math.Round(settings.FreeHeight * scale);
            var vs = GetVirtualScreenBounds();

            int fx, fy;
            if (!settings.FreePlaced)
            {
                // Primera vez: esquina inferior derecha de la pantalla elegida
                var sb = GetScreenBounds(settings.MonitorIndex);
                var tbi = GetTaskbarInfo(settings.MonitorIndex);
                fx = sb.Right - widthPx - (int)Math.Round(20 * scale);
                fy = (tbi?.Top ?? sb.Bottom) - heightF - (int)Math.Round(12 * scale);
            }
            else
            {
                fx = (int)Math.Round(settings.FreeX * scale);
                fy = (int)Math.Round(settings.FreeY * scale);
            }
            fx = Math.Clamp(fx, vs.Left, Math.Max(vs.Left, vs.Right - widthPx));
            fy = Math.Clamp(fy, vs.Top, Math.Max(vs.Top, vs.Bottom - heightF));

            SetWindowPos(hwnd, HWND_TOPMOST, fx, fy, widthPx, heightF, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            return true;
        }

        var tb = GetTaskbarInfo(settings.MonitorIndex);
        if (tb is null) return true; // sin info: no tocar nada
        if (!tb.IsVisible) return false;

        int marginPx = (int)Math.Round(3 * scale);           // margen vertical: efecto "píldora"
        int heightPx = tb.Height - marginPx * 2;
        int offsetPx = (int)Math.Round(settings.OffsetX * scale);
        int gapPx = (int)Math.Round(8 * scale);

        int x = settings.Position switch
        {
            "Left" => tb.Left + gapPx + offsetPx,
            "Center" => tb.Left + (tb.Width - widthPx) / 2 + offsetPx,
            "Custom" when settings.CustomX >= 0 => tb.Left + (int)Math.Round(settings.CustomX * scale),
            _ => tb.TrayLeft - gapPx - widthPx - offsetPx, // "Right": a la izquierda de la bandeja
        };
        int y = tb.Top + marginPx;

        // Mantener dentro de la barra (rango completo: de borde a borde)
        x = Math.Max(tb.Left, Math.Min(x, tb.Right - widthPx));

        SetWindowPos(hwnd, HWND_TOPMOST, x, y, widthPx, heightPx, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        return true;
    }

    /// <summary>Mueve la ventana a coordenadas físicas sin cambiar su tamaño (para el arrastre).</summary>
    public static void MoveTo(Window window, int xPx, int yPx)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_TOPMOST, xPx, yPx, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Rectángulo actual de la ventana en píxeles físicos.</summary>
    public static (int X, int Y, int W, int H) GetWindowBounds(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r))
            return (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        return (0, 0, 0, 0);
    }

    /// <summary>Re-afirma topmost sin mover la ventana.</summary>
    public static void AssertTopmost(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
