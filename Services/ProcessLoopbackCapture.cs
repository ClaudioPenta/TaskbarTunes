using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wasapi.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace TaskbarTunes.Services;

/// <summary>
/// Captura el audio de UN solo proceso (y sus hijos) con el loopback por
/// proceso de Windows 10 2004+ (ActivateAudioInterfaceAsync sobre el
/// dispositivo virtual VAD\Process_Loopback). Así el visualizador solo "oye"
/// a la app de música, no a juegos ni otras apps. NAudio 2.2.1 trae las
/// interfaces COM pero no el envoltorio de activación, que se hace aquí.
/// </summary>
public sealed class ProcessLoopbackCapture : IDisposable
{
    public delegate void DataHandler(byte[] buffer, int validBytes, WaveFormat format);

    private const ushort VT_BLOB = 65;
    private const int ACTIVATION_TYPE_PROCESS_LOOPBACK = 1;
    private const int MODE_INCLUDE_TARGET_PROCESS_TREE = 0;
    private static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");

    [StructLayout(LayoutKind.Sequential)]
    private struct ActivationParams
    {
        public int ActivationType;
        public uint TargetProcessId;
        public int ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlobPropVariant // PROPVARIANT con VT_BLOB
    {
        public ushort vt;
        public ushort reserved1, reserved2, reserved3;
        public uint blobSize;
        public IntPtr blobData;
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [ComVisible(true)]
    private sealed class CompletionHandler : IActivateAudioInterfaceCompletionHandler
    {
        public readonly ManualResetEventSlim Completed = new(false);
        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation) => Completed.Set();
    }

    private readonly AudioClient _client;
    private readonly WaveFormat _format;
    private readonly DataHandler _onData;
    private readonly Action? _onStopped;
    private readonly AutoResetEvent _bufferEvent = new(false);
    private Thread? _thread;
    private volatile bool _running;

    public int TargetPid { get; }

    /// <summary>Debe construirse fuera del hilo de UI (la activación exige MTA).</summary>
    public ProcessLoopbackCapture(int pid, DataHandler onData, Action? onStopped = null)
    {
        TargetPid = pid;
        _onData = onData;
        _onStopped = onStopped;

        // El loopback por proceso no expone GetMixFormat: el formato se fija
        // explícitamente y el motor de audio lo convierte.
        _format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        _client = Activate(pid);
        _client.Initialize(AudioClientShareMode.Shared,
            AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback,
            2_000_000, 0, _format, Guid.Empty);
        _client.SetEventHandle(_bufferEvent.SafeWaitHandle.DangerousGetHandle());
    }

    private static AudioClient Activate(int pid)
    {
        var ap = new ActivationParams
        {
            ActivationType = ACTIVATION_TYPE_PROCESS_LOOPBACK,
            TargetProcessId = (uint)pid,
            ProcessLoopbackMode = MODE_INCLUDE_TARGET_PROCESS_TREE,
        };

        int apSize = Marshal.SizeOf<ActivationParams>();
        IntPtr apPtr = Marshal.AllocHGlobal(apSize);
        IntPtr pvPtr = IntPtr.Zero;
        try
        {
            Marshal.StructureToPtr(ap, apPtr, false);
            var pv = new BlobPropVariant { vt = VT_BLOB, blobSize = (uint)apSize, blobData = apPtr };
            pvPtr = Marshal.AllocHGlobal(Marshal.SizeOf<BlobPropVariant>());
            Marshal.StructureToPtr(pv, pvPtr, false);

            var handler = new CompletionHandler();
            var iid = IID_IAudioClient;
            ActivateAudioInterfaceAsync(@"VAD\Process_Loopback", ref iid, pvPtr, handler, out var operation);

            if (!handler.Completed.Wait(3000))
                throw new TimeoutException("ActivateAudioInterfaceAsync no completó a tiempo");

            operation.GetActivateResult(out int hr, out object activated);
            Marshal.ThrowExceptionForHR(hr);
            return new AudioClient((IAudioClient)activated);
        }
        finally
        {
            if (pvPtr != IntPtr.Zero) Marshal.FreeHGlobal(pvPtr);
            Marshal.FreeHGlobal(apPtr);
        }
    }

    public void Start()
    {
        _running = true;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "ProcessLoopbackCapture" };
        _thread.Start();
        _client.Start();
    }

    private void CaptureLoop()
    {
        byte[] temp = Array.Empty<byte>();
        try
        {
            var capture = _client.AudioCaptureClient;
            while (_running)
            {
                _bufferEvent.WaitOne(100);
                if (!_running) break;

                while (_running && capture.GetNextPacketSize() > 0)
                {
                    IntPtr ptr = capture.GetBuffer(out int frames, out AudioClientBufferFlags flags);
                    int bytes = frames * _format.BlockAlign;
                    if (temp.Length < bytes) temp = new byte[bytes];
                    if ((flags & AudioClientBufferFlags.Silent) != 0)
                        Array.Clear(temp, 0, bytes);
                    else
                        Marshal.Copy(ptr, temp, 0, bytes);
                    capture.ReleaseBuffer(frames);
                    if (bytes > 0) _onData(temp, bytes, _format);
                }
            }
        }
        catch
        {
            // El proceso objetivo murió o el cliente se invalidó
            if (_running) _onStopped?.Invoke();
        }
    }

    public void Dispose()
    {
        _running = false;
        _bufferEvent.Set();
        try { _thread?.Join(500); } catch { }
        try { _client.Stop(); } catch { }
        try { _client.Dispose(); } catch { }
        _bufferEvent.Dispose();
    }
}
