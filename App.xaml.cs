// TaskbarTunes — music widget for the Windows 11 taskbar.
// Copyright (C) 2026 ClaudioPenta
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It comes with ABSOLUTELY NO WARRANTY. See the LICENSE file, or
// <https://www.gnu.org/licenses/>, for details.

using System.IO;
using System.Windows;
using Microsoft.Win32;
using TaskbarTunes.Helpers;
using TaskbarTunes.Services;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace TaskbarTunes;

public partial class App : Application
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "TaskbarTunes";

    private static readonly string LogPath = AppPaths.ErrorLogFile;

    private Mutex? _mutex;
    private SettingsService _settings = null!;
    private MediaSessionService _media = null!;
    private AudioCaptureService _audio = null!;
    private HistoryService _history = null!;
    private OverlayWindow? _overlay;
    private SettingsWindow? _settingsWindow;
    private WinForms.NotifyIcon? _tray;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        _mutex = new Mutex(true, "TaskbarTunes_SingleInstance", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            args.Handled = true; // el widget no debe tumbar la sesión por un fallo puntual
        };

        AppPaths.MigrateLegacyData(); // primer arranque tras el renombrado WidgetMusic → TaskbarTunes

        _settings = new SettingsService();
        _settings.Load();

        _media = new MediaSessionService(Dispatcher, _settings);
        _audio = new AudioCaptureService();

        _history = new HistoryService();
        _history.Load();
        _media.TrackChanged += info =>
        {
            if (info is not null) _history.Add(info);
            ConfigureAudio(); // la app de origen pudo cambiar (Spotify ↔ navegador)
        };

        _overlay = new OverlayWindow(_settings, _media, _audio, _history);
        _overlay.Show();

        _settings.Changed += OnSettingsChanged;
        ConfigureAudio();
        CreateTrayIcon();

        try { await _media.StartAsync(); }
        catch (Exception ex) { Log(ex); }
    }

    private void OnSettingsChanged()
    {
        _overlay?.ApplySettings();
        ConfigureAudio();
        _media.Repick();
    }

    private void ConfigureAudio()
    {
        var s = _settings.Settings;
        _audio.Configure(s.ShowVisualizer, s.VisualizerAudioSource, _media.CurrentSourceAppId);
    }

    public void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ExitApp()
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        _audio.Dispose();
        _media.Dispose();
        _settings.Save();
        Shutdown();
    }

    // ----- Autoarranque con Windows (HKCU\...\Run) -----

    public static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is not null;
    }

    public static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enable && Environment.ProcessPath is string exe)
                key.SetValue(RunValueName, $"\"{exe}\"");
            else
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) { Log(ex); }
    }

    // ----- Icono de la bandeja del sistema -----

    private void CreateTrayIcon()
    {
        _tray = new WinForms.NotifyIcon
        {
            Icon = CreateTrayIconImage(),
            Text = "TaskbarTunes",
            Visible = true,
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Ajustes…", null, (_, _) => Dispatcher.Invoke(OpenSettings));

        var autoStart = new WinForms.ToolStripMenuItem("Iniciar con Windows")
        {
            CheckOnClick = true,
            Checked = IsAutoStartEnabled(),
        };
        autoStart.CheckedChanged += (_, _) => SetAutoStart(autoStart.Checked);
        menu.Items.Add(autoStart);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Dispatcher.Invoke(ExitApp));

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
    }

    private static Drawing.Icon CreateTrayIconImage()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Drawing.Color.Transparent);

            using var circle = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 29, 185, 84));
            g.FillEllipse(circle, 1, 1, 30, 30);

            using var font = new Drawing.Font("Segoe UI Symbol", 17, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            using var fmt = new Drawing.StringFormat
            {
                Alignment = Drawing.StringAlignment.Center,
                LineAlignment = Drawing.StringAlignment.Center,
            };
            g.DrawString("♪", font, Drawing.Brushes.White, new Drawing.RectangleF(0, 1, 32, 32), fmt);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }
}
