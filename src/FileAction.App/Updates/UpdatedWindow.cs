using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FileAction.App.Diagnostics;
using FileAction.App.UI;
using FileAction.Core.Updates;
using static FileAction.Core.Loc;

namespace FileAction.App.Updates;

/// <summary>
/// The "DropWheel updated to x.y.z" message shown once after the helper restarts the app with
/// <c>--updated &lt;version&gt;</c>. It offers the release page as "What's new" and a plain Close action.
/// </summary>
internal static class UpdatedWindow
{
    /// <summary>Builds and shows the announcement; called by <see cref="UpdateApplier.AnnounceUpdated"/>.</summary>
    public static void Show(string version)
    {
        var title = T("update.updated.title");
        var body = T("update.updated.body", Bidi.Value(DisplayVersion(version)));
        Theme.Refresh();

        var window = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Theme.Page,
            FontFamily = Theme.UiFont,
        };
        UiSetup.Configure(window);

        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(Controls.Text(title, 20, FontWeights.SemiBold));

        var text = Controls.Text(body, 14);
        text.Margin = new Thickness(0, 10, 0, 22);
        content.Children.Add(text);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var whatsNew = Controls.Button(T("update.updated.whatsNew"), () => OpenReleasePage(version), primary: true);
        buttons.Children.Add(whatsNew);
        // An explicit spacer keeps the gap between the buttons in both LTR and RTL layouts.
        buttons.Children.Add(new Border { Width = 8 });
        buttons.Children.Add(Controls.Button(T("action.close"), window.Close));
        content.Children.Add(buttons);

        window.Content = content;
        window.Show();
    }

    /// <summary>The release page for the installed version, e.g. …/releases/tag/v2.2.0.</summary>
    internal static string ReleaseUrl(string version) =>
        string.Format(CultureInfo.InvariantCulture, UpdateConfig.ReleasePageFormat, "v" + DisplayVersion(version));

    /// <summary>Normalizes "v2.2.0", "2.2" and "2.2.0.0" to the 2.2.0 the release page uses.</summary>
    internal static string DisplayVersion(string version)
    {
        var value = (version ?? "").Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];
        return SemanticVersion.TryParse(value, out var parsed) ? parsed.ToString(3) : value;
    }

    private static void OpenReleasePage(string version)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ReleaseUrl(version)) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            AppLog.Error("update apply: could not open the release page", ex);
        }
    }
}
