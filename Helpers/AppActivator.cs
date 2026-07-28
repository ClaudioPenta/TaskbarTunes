using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TaskbarTunes.Helpers;

/// <summary>Trae al frente la app de música (acción de doble clic).</summary>
public static class AppActivator
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    private const int SW_RESTORE = 9;

    private static readonly string[] BrowserProcesses =
        { "brave", "chrome", "msedge", "firefox", "opera", "vivaldi", "zen", "arc" };

    public static void OpenSpotify()
    {
        try { Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true }); }
        catch { }
    }

    /// <summary>
    /// Activa una ventana propia. Necesario antes de abrir el menú contextual
    /// del widget: con WS_EX_NOACTIVATE el menú no captura el ratón y no se
    /// cierra al hacer clic fuera (SetForegroundWindow sí puede activarla).
    /// </summary>
    public static void FocusWindow(System.Windows.Window window)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);
    }

    /// <summary>Activa la primera ventana de navegador que encuentre (mejor esfuerzo).</summary>
    public static void ActivateBrowser()
    {
        foreach (var name in BrowserProcesses)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    if (IsIconic(p.MainWindowHandle)) ShowWindow(p.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(p.MainWindowHandle);
                    return;
                }
                catch { }
            }
        }
    }
}
