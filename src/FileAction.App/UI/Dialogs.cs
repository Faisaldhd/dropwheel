using System.Windows;

namespace FileAction.App.UI;

/// <summary>
/// Message boxes with the right reading direction for Arabic. Native message boxes follow the
/// Windows UI language; the RTL flags make the text and alignment follow the app language instead,
/// and older Windows versions that reject the flags simply get a normal box.
/// </summary>
internal static class Dialogs
{
    private static MessageBoxOptions Direction =>
        LanguageManager.IsRtl ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None;

    public static MessageBoxResult Show(string text, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        try
        {
            return MessageBox.Show(text, title, buttons, image, defaultResult, Direction);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return MessageBox.Show(text, title, buttons, image, defaultResult);
        }
    }

    public static MessageBoxResult Show(Window? owner, string text, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        if (owner is null)
            return Show(text, title, buttons, image, defaultResult);
        try
        {
            return MessageBox.Show(owner, text, title, buttons, image, defaultResult, Direction);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return MessageBox.Show(owner, text, title, buttons, image, defaultResult);
        }
    }
}
