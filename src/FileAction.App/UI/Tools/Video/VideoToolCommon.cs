using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FileAction.Core;
using FileAction.Core.Backends;
using Microsoft.Win32;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// Rebuilds a tool's view when the palette changes while its window is open: the app's theme (a
/// Settings change) is noticed when the window is activated again, and a Windows light/dark switch is
/// noticed through the user-preference broadcast. Both paths call <see cref="Theme.Refresh"/> and then
/// the view's repaint callback, which re-creates its elements so every brush comes from the new palette.
/// </summary>
internal sealed class ThemeWatcher
{
    private readonly FrameworkElement _root;
    private readonly Action _repaint;
    private Window? _window;
    private bool _hooked;
    private bool _dark;

    public ThemeWatcher(FrameworkElement root, Action repaint)
    {
        _root = root;
        _repaint = repaint;
    }

    public void Start()
    {
        if (_hooked)
            return;
        _hooked = true;
        _dark = Theme.IsDark;
        _window = Window.GetWindow(_root);
        if (_window is not null)
            _window.Activated += OnActivated;
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    public void Stop()
    {
        if (!_hooked)
            return;
        _hooked = false;
        if (_window is not null)
            _window.Activated -= OnActivated;
        _window = null;
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
    }

    private void OnActivated(object? sender, EventArgs e) => Check();

    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
            return;
        // The broadcast arrives on its own thread; the palette and the visuals belong to the UI thread.
        _root.Dispatcher.InvokeAsync(Check);
    }

    private void Check()
    {
        Theme.Refresh();
        if (Theme.IsDark == _dark)
            return;
        _dark = Theme.IsDark;
        _repaint();
    }
}


/// <summary>Shared helpers for the Split and Join windows: probing, frame thumbnails and time labels.</summary>
internal static partial class VideoToolCommon
{
    [GeneratedRegex(@"\btime=\s*(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex TimePattern();

    /// <summary>"0:07.5" under an hour and "1:02:03" above it; Western digits in both languages.</summary>
    public static string Time(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss\.f", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Removes a control from whatever still holds it, so a view that rebuilds its layout on a theme
    /// change can put the same state-bearing control into a new parent without re-creating it.
    /// </summary>
    public static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case Border border when ReferenceEquals(border.Child, element):
                border.Child = null;
                break;
            case ContentControl content when ReferenceEquals(content.Content, element):
                content.Content = null;
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
        }
    }

    /// <summary>
    /// Reads length, video size, frame rate and audio presence from what <c>ffmpeg -i</c> prints.
    /// Uses the same parser the unit tests cover; a file ffmpeg cannot read comes back unusable.
    /// </summary>
    public static async Task<ClipInfo> ProbeAsync(string ffmpeg, string path, CancellationToken ct)
    {
        var lines = new List<string>();
        await ProcessRunner.RunAsync(
            ffmpeg,
            ["-hide_banner", "-nostdin", "-i", path],
            ct,
            line =>
            {
                lock (lines)
                    lines.Add(line);
            },
            // "-i file" without an output exits with 1 by design; only its banner is wanted.
            throwOnError: false).ConfigureAwait(false);

        string[] snapshot;
        lock (lines)
            snapshot = [.. lines];
        return VideoSplitJoin.ParseProbe(path, snapshot);
    }

    /// <summary>The seconds an ffmpeg progress line reports ("time=00:00:12.34"), or null.</summary>
    public static double? ProgressSeconds(string line)
    {
        if (TimePattern().Match(line) is not { Success: true } match)
            return null;
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
            + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60
            + double.Parse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>One frame of a clip as a frozen bitmap, or null when ffmpeg could not decode it.</summary>
    public static async Task<BitmapSource?> FrameAsync(
        string ffmpeg, string path, double seconds, int width, CancellationToken ct)
    {
        var folder = OutputPaths.TempDirectory();
        try
        {
            var file = Path.Combine(folder, "frame.jpg");
            if (!await FfmpegBackend.ExtractFrameAsync(ffmpeg, path, seconds, width, file, ct).ConfigureAwait(false))
                return null;
            return Decode(file);
        }
        catch (ConversionException)
        {
            return null;
        }
        finally
        {
            OutputPaths.TryDelete(folder);
        }
    }

    private static BitmapSource? Decode(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            // A frame that cannot be decoded is simply not shown; it is never a failure of the tool.
            return null;
        }
    }
}
