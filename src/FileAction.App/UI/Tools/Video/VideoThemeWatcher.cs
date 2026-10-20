using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// Detects a theme change while a video tool is open: an OS light/dark switch (SystemEvents) and the
/// app's own Settings → Theme choice (<see cref="Theme.IsDark"/> changes after Theme.Apply). Both paths call
/// <see cref="Theme.Refresh"/> and then raise <see cref="Changed"/> on the UI thread, where the views retint
/// themselves instead of reopening and losing state.
/// </summary>
internal static class VideoThemeWatcher
{
    private static readonly object Gate = new();
    private static DispatcherTimer? _timer;
    private static bool _lastDark;
    private static int _users;

    /// <summary>Raised on the UI thread after <see cref="Theme.Refresh"/>; subscribers retint their tree.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Starts the theme watch for one open video surface. The poll exists only while at least one
    /// surface holds it; <see cref="Stop"/> is the matching release (see the three video views).
    /// </summary>
    public static void Start()
    {
        lock (Gate)
        {
            _users++;
            if (_timer is not null)
                return;

            _lastDark = Theme.IsDark;
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500),
            };
            _timer.Tick += (_, _) =>
            {
                // Theme.Apply (Settings) changes IsDark without a system event.
                if (Theme.IsDark != _lastDark)
                    RefreshAndRaise();
            };
            _timer.Start();

            try
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            }
            catch (InvalidOperationException)
            {
                // No interactive window station: the polling tick still covers app-level theme changes.
            }
        }
    }

    /// <summary>
    /// Releases one surface's hold on the watch. The last one to leave stops the timer and unhooks the
    /// system event, so a closed video tool leaves no 500 ms poll behind (the idle-CPU rule).
    /// </summary>
    public static void Stop()
    {
        lock (Gate)
        {
            if (_users > 0)
                _users--;
            if (_users > 0 || _timer is null)
                return;

            _timer.Stop();
            _timer = null;
            try
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color))
            return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;
        dispatcher.BeginInvoke(RefreshAndRaise);
    }

    private static void RefreshAndRaise()
    {
        Theme.Refresh();
        _lastDark = Theme.IsDark;
        Changed?.Invoke();
    }
}
