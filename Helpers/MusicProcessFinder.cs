using System.Diagnostics;
using System.IO;

namespace TaskbarTunes.Helpers;

/// <summary>
/// Resuelve el proceso raíz de la app de música a partir del identificador de
/// la sesión de medios (AUMID). Con INCLUDE_TARGET_PROCESS_TREE basta capturar
/// la raíz: los procesos hijos de audio del navegador quedan incluidos.
/// </summary>
public static class MusicProcessFinder
{
    private static readonly (string Key, string[] Processes)[] Map =
    {
        ("spotify", new[] { "Spotify" }),
        ("brave", new[] { "brave" }),
        ("chrome", new[] { "chrome" }),
        ("edge", new[] { "msedge" }),
        ("firefox", new[] { "firefox" }),
        ("opera", new[] { "opera" }),
        ("vivaldi", new[] { "vivaldi" }),
        ("zen", new[] { "zen" }),
        ("arc", new[] { "Arc" }),
        ("chromium", new[] { "chromium" }),
    };

    public static int? FindPid(string sourceAppId)
    {
        string id = sourceAppId.ToLowerInvariant();

        foreach (var (key, names) in Map)
        {
            if (!id.Contains(key)) continue;
            foreach (var name in names)
            {
                if (FindRootPid(name) is int pid) return pid;
            }
        }

        // AUMID de app clásica tipo "reproductor.exe": probar el nombre directo
        if (id.EndsWith(".exe"))
            return FindRootPid(Path.GetFileNameWithoutExtension(id));

        return null;
    }

    private static int? FindRootPid(string processName)
    {
        Process[] procs;
        try { procs = Process.GetProcessesByName(processName); }
        catch { return null; }

        try
        {
            // El proceso raíz suele ser el que tiene la ventana principal;
            // los hijos (audio del navegador, etc.) no la tienen.
            int? result = null;
            foreach (var p in procs)
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero) { result = p.Id; break; }
                }
                catch { }
            }
            if (result is null && procs.Length > 0) result = procs[0].Id;
            return result;
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
        }
    }
}
