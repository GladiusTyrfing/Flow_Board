using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NAudio.Wave;

namespace FlowBoard.Services;

/// <summary>Records voice notes from the default microphone to a WAV file.</summary>
public sealed partial class AudioRecorder : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _tick;
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _path;
    private DateTime _started;
    private volatile float _peak;
    private TaskCompletionSource<bool>? _stopped;

    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private double _level;
    [ObservableProperty] private string _elapsedText = "0:00";

    public AudioRecorder()
    {
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _tick.Tick += (_, _) =>
        {
            // Smooth the meter: rise instantly, fall gently.
            var p = _peak;
            _peak = 0;
            Level = p > Level ? p : Level * 0.8;
            ElapsedText = (DateTime.Now - _started).ToString(@"m\:ss");
        };
    }

    public static bool HasMicrophone
    {
        get
        {
            try { return WaveInEvent.DeviceCount > 0; }
            catch { return false; }
        }
    }

    public void Start()
    {
        if (IsRecording) return;
        _path = Path.Combine(AppPaths.TempDir, $"rec-{Guid.NewGuid():N}.wav");
        _waveIn = new WaveInEvent { WaveFormat = new WaveFormat(22050, 16, 1), BufferMilliseconds = 50 };
        _writer = new WaveFileWriter(_path, _waveIn.WaveFormat);
        _stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _waveIn.DataAvailable += (_, e) =>
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
            float max = 0;
            for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                var sample = Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768f);
                if (sample > max) max = sample;
            }

            if (max > _peak) _peak = max;
        };
        _waveIn.RecordingStopped += (_, _) =>
        {
            _writer?.Dispose();
            _writer = null;
            _waveIn?.Dispose();
            _waveIn = null;
            _stopped?.TrySetResult(true);
        };

        _started = DateTime.Now;
        _waveIn.StartRecording();
        IsRecording = true;
        _tick.Start();
    }

    /// <summary>Stops recording and returns the temp WAV path and its duration.</summary>
    public async Task<(string Path, TimeSpan Duration)?> StopAsync()
    {
        if (!IsRecording || _waveIn == null || _path == null) return null;
        var duration = DateTime.Now - _started;
        _waveIn.StopRecording();
        if (_stopped != null) await Task.WhenAny(_stopped.Task, Task.Delay(2000));
        Reset();
        return (_path, duration);
    }

    public async Task CancelAsync()
    {
        var result = await StopAsync();
        if (result is { } r)
        {
            try { File.Delete(r.Path); } catch { }
        }
    }

    private void Reset()
    {
        _tick.Stop();
        IsRecording = false;
        Level = 0;
        ElapsedText = "0:00";
    }

    public void Dispose()
    {
        try { _waveIn?.StopRecording(); } catch { }
        _writer?.Dispose();
        _waveIn?.Dispose();
    }
}
