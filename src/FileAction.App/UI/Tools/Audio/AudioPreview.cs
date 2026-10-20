using System.Windows.Media;
using System.Windows.Threading;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// Plays the tool's source file through WPF's MediaPlayer and reports the position, so both audio windows
/// can share one set of open/play/pause/failure rules. Formats MediaPlayer cannot decode report
/// <see cref="Failed"/> once and stay silent afterwards.
/// </summary>
internal sealed class AudioPreview
{
    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly string _path;

    private bool _opening;
    private bool _closed;
    private bool _broken;
    private double _start;
    private double _stopAt = double.PositiveInfinity;

    public AudioPreview(string path)
    {
        _path = path;
        _player.MediaOpened += (_, _) => _dispatcher.BeginInvoke(new Action(StartPending));
        _player.MediaFailed += (_, _) => _dispatcher.BeginInvoke(new Action(Fail));
        _player.MediaEnded += (_, _) => _dispatcher.BeginInvoke(new Action(Stop));
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Position while playing, in seconds (also reported once when playback starts).</summary>
    public event Action<double>? PositionChanged;

    /// <summary>Playback stopped or was paused; the view clears its playhead.</summary>
    public event Action? Stopped;

    /// <summary>Play/pause state changed (used to swap the button caption).</summary>
    public event Action? StateChanged;

    /// <summary>The format cannot be previewed; the view disables its play button.</summary>
    public event Action? Failed;

    public bool IsPlaying { get; private set; }

    public bool IsAvailable => !_broken;

    /// <summary>
    /// Starts from <paramref name="from"/> and stops at <paramref name="stopAt"/>; calling it while playing
    /// stops instead, which is how the play/pause button toggles.
    /// </summary>
    public void Play(double from, double stopAt)
    {
        if (_broken || _opening)
            return;
        if (IsPlaying)
        {
            Stop();
            return;
        }

        _start = Math.Max(0, from);
        _stopAt = double.IsFinite(stopAt) && stopAt > _start ? stopAt : double.PositiveInfinity;

        if (_closed || _player.Source is null)
        {
            _opening = true;
            _closed = false;
            _player.Open(new Uri(_path));
            return;
        }

        Begin();
    }

    public void Stop()
    {
        _timer.Stop();
        IsPlaying = false;
        try
        {
            if (_player.Source is not null)
                _player.Pause();
        }
        catch (InvalidOperationException)
        {
            // The media never opened; there is nothing to pause.
        }
        Stopped?.Invoke();
        StateChanged?.Invoke();
    }

    /// <summary>Releases the file handle (FFmpeg is about to read the same source).</summary>
    public void Close()
    {
        Stop();
        try
        {
            if (_player.Source is not null)
                _player.Close();
        }
        catch (InvalidOperationException)
        {
            // Already closed.
        }
        _closed = true;
    }

    private void StartPending()
    {
        _opening = false;
        Begin();
    }

    private void Begin()
    {
        if (_broken)
            return;
        try
        {
            _player.Position = TimeSpan.FromSeconds(_start);
            _player.Play();
            IsPlaying = true;
            _timer.Start();
            StateChanged?.Invoke();
            PositionChanged?.Invoke(_start);
        }
        catch (InvalidOperationException)
        {
            Fail();
        }
    }

    private void Tick()
    {
        var position = _player.Position.TotalSeconds;
        PositionChanged?.Invoke(position);
        if (position >= _stopAt - 0.02)
            Stop();
    }

    private void Fail()
    {
        _broken = true;
        _opening = false;
        _timer.Stop();
        IsPlaying = false;
        Stopped?.Invoke();
        Failed?.Invoke();
    }
}
