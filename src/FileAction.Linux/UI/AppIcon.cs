using Avalonia.Controls;
using Avalonia.Platform;

namespace FileAction.Linux.UI;

internal static class AppIcon
{
    public static WindowIcon? Window
    {
        get
        {
            try
            {
                return new WindowIcon(AssetLoader.Open(new Uri("avares://dropwheel/Assets/dropwheel.png")));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
