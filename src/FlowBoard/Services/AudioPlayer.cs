using System.Windows.Threading;
using FlowBoard.Models;
using NAudio.Wave;

namespace FlowBoard.Services;

/// <summary>Plays one voice note at a time and reports progress on the attachment itself.</summary>
public sealed class AudioPlayer : IDisposable
{
    private readonly DispatcherTimer _tick;
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private Attachment? _current;

    public AudioPlayer()
    {
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _tick.Tick += (_, _) => UpdateProgress();
    }

    public void Toggle(Attachment attachment)
    {
        if (_current == attachment && _output != null)
        {
            if (_output.PlaybackState == PlaybackState.Playing)
            {
                _output.Pause();
                attachment.IsPlaying = false;
            }
            else
            {
                _output.Play();
                attachment.IsPlaying = true;
            }

            return;
        }

        Stop();
        if (attachment.FullPath is not { } path || !System.IO.File.Exists(path)) return;

        _reader = new AudioFileReader(path);
        _output = new WaveOutEvent();
        _output.Init(_reader);
        _output.PlaybackStopped += OnPlaybackStopped;
        _current = attachment;
        if (attachment.DurationSeconds <= 0) attachment.DurationSeconds = _reader.TotalTime.TotalSeconds;
        _output.Play();
        attachment.IsPlaying = true;
        _tick.Start();
    }

    public void Seek(Attachment attachment, double fraction)
    {
        if (_current != attachment || _reader == null) return;
        _reader.CurrentTime = TimeSpan.FromSeconds(_reader.TotalTime.TotalSeconds * Math.Clamp(fraction, 0, 1));
        UpdateProgress();
    }

    public void Stop()
    {
        _tick.Stop();
        if (_output != null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Stop();
            _output.Dispose();
            _output = null;
        }

        _reader?.Dispose();
        _reader = null;
        if (_current != null)
        {
            _current.IsPlaying = false;
            _current.PlaybackProgress = 0;
            _current.PlaybackPositionText = string.Empty;
            _current = null;
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e) => Stop();

    private void UpdateProgress()
    {
        if (_current == null || _reader == null) return;
        var total = _reader.TotalTime.TotalSeconds;
        _current.PlaybackProgress = total > 0 ? _reader.CurrentTime.TotalSeconds / total : 0;
        _current.PlaybackPositionText = _reader.CurrentTime.ToString(@"m\:ss");
    }

    public void Dispose() => Stop();
}
