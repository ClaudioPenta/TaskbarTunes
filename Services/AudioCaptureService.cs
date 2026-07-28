using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Dsp;
using NAudio.Wave;
using TaskbarTunes.Helpers;

namespace TaskbarTunes.Services;

/// <summary>
/// Convierte audio en niveles por banda de frecuencia (FFT + escala logarítmica
/// + auto-ganancia) para el visualizador. Dos modos:
///  - "App": captura SOLO el proceso de la app de música (loopback por proceso)
///    — los juegos y demás apps no mueven el visualizador. Si el proceso no se
///    puede resolver, cae al modo sistema; si no hay música, queda en reposo.
///  - "System": captura todo lo que suena (loopback clásico del dispositivo).
/// </summary>
public class AudioCaptureService : IDisposable
{
    private const int FftSize = 1024;
    private const int FftM = 10; // log2(1024)
    public const int BandCount = 64;
    private const double MinFreq = 50, MaxFreq = 12000;

    private readonly object _lock = new();
    private readonly float[] _bands = new float[BandCount];
    private readonly float[] _ring = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];

    private WasapiLoopbackCapture? _systemCapture;
    private ProcessLoopbackCapture? _processCapture;
    private MMDeviceEnumerator? _enumerator;
    private DeviceNotifier? _notifier;
    private System.Threading.Timer? _retryTimer;

    private int _ringPos;
    private int _newSamples;
    private float _gain = 0.05f; // auto-ganancia: pico reciente
    private long _lastDataTicks;
    private volatile bool _disposed;

    // Estado deseado (lo fija Configure; Reconfigure lo materializa)
    private bool _enabled;
    private string _mode = "App";
    private string? _aumid;
    private int _generation;

    /// <summary>
    /// Fija el estado deseado y lo aplica en segundo plano. Llamar en cada
    /// cambio de ajustes y de pista (el AUMID de la fuente puede cambiar).
    /// </summary>
    public void Configure(bool enabled, string mode, string? sourceAppId)
    {
        lock (_lock)
        {
            _enabled = enabled;
            _mode = mode;
            _aumid = sourceAppId;
        }
        int gen = Interlocked.Increment(ref _generation);
        Task.Run(() => Reconfigure(gen)); // MTA (necesario para la activación COM) y fuera del hilo de UI
    }

    private void Reconfigure(int gen)
    {
        lock (_lock)
        {
            if (_disposed || gen != _generation) return;

            if (!_enabled)
            {
                CleanupCaptures();
                Array.Clear(_bands);
                return;
            }

            if (_mode == "App")
            {
                if (_aumid is null)
                {
                    // Sin sesión de música: visualizador en reposo
                    CleanupCaptures();
                    Array.Clear(_bands);
                    return;
                }

                int? pid = MusicProcessFinder.FindPid(_aumid);
                if (pid is int p)
                {
                    if (_processCapture?.TargetPid == p) return; // ya capturando ese proceso

                    CleanupCaptures();
                    try
                    {
                        _processCapture = new ProcessLoopbackCapture(p, PushBuffer,
                            onStopped: () => ScheduleRestart(1000));
                        _processCapture.Start();
                        return;
                    }
                    catch
                    {
                        // Windows sin soporte o activación fallida → modo sistema
                        _processCapture?.Dispose();
                        _processCapture = null;
                    }
                }
                // PID no resuelto (p.ej. Firefox) → fallback al modo sistema
            }

            if (_systemCapture is not null) return; // sistema ya activo
            CleanupCaptures();
            StartSystemCapture();
        }
    }

    private void StartSystemCapture()
    {
        try
        {
            _systemCapture = new WasapiLoopbackCapture();
            _systemCapture.DataAvailable += OnSystemData;
            _systemCapture.RecordingStopped += OnSystemStopped;
            _systemCapture.StartRecording();

            if (_enumerator is null)
            {
                _enumerator = new MMDeviceEnumerator();
                _notifier = new DeviceNotifier(this);
                _enumerator.RegisterEndpointNotificationCallback(_notifier);
            }
        }
        catch
        {
            CleanupCaptures();
            ScheduleRestart(3000); // sin dispositivo de salida: reintentar
        }
    }

    private void CleanupCaptures()
    {
        if (_systemCapture is not null)
        {
            try { _systemCapture.StopRecording(); } catch { }
            _systemCapture.DataAvailable -= OnSystemData;
            _systemCapture.RecordingStopped -= OnSystemStopped;
            try { _systemCapture.Dispose(); } catch { }
            _systemCapture = null;
        }
        if (_processCapture is not null)
        {
            try { _processCapture.Dispose(); } catch { }
            _processCapture = null;
        }
    }

    /// <summary>Reinicia la captura (cambio de dispositivo, proceso muerto, error).</summary>
    internal void ScheduleRestart(int delayMs)
    {
        if (_disposed || !_enabled) return;
        int gen = Interlocked.Increment(ref _generation);
        _retryTimer?.Dispose();
        _retryTimer = new System.Threading.Timer(_ =>
        {
            lock (_lock) CleanupCaptures();
            Reconfigure(gen);
        }, null, delayMs, Timeout.Infinite);
    }

    private void OnSystemStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null && _enabled && !_disposed)
            ScheduleRestart(1000);
    }

    private void OnSystemData(object? sender, WaveInEventArgs e)
    {
        var wf = _systemCapture?.WaveFormat;
        if (wf is not null) PushBuffer(e.Buffer, e.BytesRecorded, wf);
    }

    /// <summary>Entrada común de muestras (sistema o proceso): mezcla a mono y alimenta la FFT.</summary>
    private void PushBuffer(byte[] buffer, int validBytes, WaveFormat wf)
    {
        if (wf.BitsPerSample != 32) return; // ambas rutas entregan IEEE float 32

        int channels = Math.Max(1, wf.Channels);
        int frames = validBytes / 4 / channels;

        for (int f = 0; f < frames; f++)
        {
            float mono = 0;
            for (int c = 0; c < channels; c++)
                mono += BitConverter.ToSingle(buffer, (f * channels + c) * 4);
            mono /= channels;

            _ring[_ringPos] = mono;
            _ringPos = (_ringPos + 1) % FftSize;
            _newSamples++;
        }

        if (_newSamples >= FftSize / 2)
        {
            _newSamples = 0;
            ComputeBands(wf.SampleRate);
        }
    }

    private void ComputeBands(int sampleRate)
    {
        // Muestras en orden cronológico + ventana Hann
        for (int i = 0; i < FftSize; i++)
        {
            float sample = _ring[(_ringPos + i) % FftSize];
            _fft[i].X = sample * (float)FastFourierTransform.HannWindow(i, FftSize);
            _fft[i].Y = 0;
        }
        FastFourierTransform.FFT(true, FftM, _fft);

        double binHz = (double)sampleRate / FftSize;
        double maxFreq = Math.Min(MaxFreq, sampleRate / 2.0 - binHz);
        float frameMax = 0;
        Span<float> raw = stackalloc float[BandCount];

        for (int b = 0; b < BandCount; b++)
        {
            // Bandas en escala logarítmica entre MinFreq y maxFreq
            double f0 = MinFreq * Math.Pow(maxFreq / MinFreq, (double)b / BandCount);
            double f1 = MinFreq * Math.Pow(maxFreq / MinFreq, (double)(b + 1) / BandCount);
            int i0 = Math.Max(1, (int)(f0 / binHz));
            int i1 = Math.Max(i0 + 1, (int)Math.Ceiling(f1 / binHz));
            i1 = Math.Min(i1, FftSize / 2);

            float mag = 0;
            for (int i = i0; i < i1; i++)
            {
                float m = MathF.Sqrt(_fft[i].X * _fft[i].X + _fft[i].Y * _fft[i].Y);
                if (m > mag) mag = m;
            }
            raw[b] = mag;
            if (mag > frameMax) frameMax = mag;
        }

        // Auto-ganancia: sigue el pico reciente, con suelo para no amplificar el ruido
        _gain = Math.Max(Math.Max(frameMax, _gain * 0.985f), 0.002f);

        lock (_lock)
        {
            for (int b = 0; b < BandCount; b++)
                _bands[b] = MathF.Sqrt(Math.Min(1f, raw[b] / _gain)); // sqrt: curva perceptual
            _lastDataTicks = Environment.TickCount64;
        }
    }

    /// <summary>Copia los niveles actuales (0..1) en <paramref name="dest"/> (longitud 64).</summary>
    public void ReadBands(float[] dest)
    {
        lock (_lock)
        {
            // Sin datos recientes (silencio/pausa): ambas rutas dejan de entregar
            // buffers, así que forzamos el reposo.
            if (Environment.TickCount64 - _lastDataTicks > 300)
                Array.Clear(dest);
            else
                Array.Copy(_bands, dest, Math.Min(dest.Length, _bands.Length));
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _retryTimer?.Dispose();
        lock (_lock)
        {
            if (_enumerator is not null && _notifier is not null)
            {
                try { _enumerator.UnregisterEndpointNotificationCallback(_notifier); } catch { }
                _enumerator.Dispose();
            }
            CleanupCaptures();
        }
    }

    /// <summary>Reinicia la captura de sistema cuando cambia el dispositivo de salida por defecto.</summary>
    private sealed class DeviceNotifier : IMMNotificationClient
    {
        private readonly AudioCaptureService _owner;
        public DeviceNotifier(AudioCaptureService owner) => _owner = owner;

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
                _owner.ScheduleRestart(500);
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
