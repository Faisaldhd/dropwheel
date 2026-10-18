using System.Globalization;
using System.Windows;
using FileAction.Core;

namespace FileAction.App.UI;

/// <summary>
/// Turns the saved <see cref="UiLanguage"/> preference into the language and reading direction the
/// UI uses, and applies that direction to windows. <see cref="UiLanguage.System"/> follows the
/// Windows display language.
/// </summary>
internal static class LanguageManager
{
    /// <summary>What the user chose in Settings (may be <see cref="UiLanguage.System"/>).</summary>
    public static UiLanguage Preference { get; private set; } = UiLanguage.System;

    /// <summary>The language actually used; never <see cref="UiLanguage.System"/>.</summary>
    public static UiLanguage Current { get; private set; } = UiLanguage.English;

    public static bool IsRtl => Current == UiLanguage.Arabic;

    public static System.Windows.FlowDirection FlowDirection => IsRtl
        ? System.Windows.FlowDirection.RightToLeft
        : System.Windows.FlowDirection.LeftToRight;

    /// <summary>The Windows display language (falls back to the installed language when neutral).</summary>
    public static string SystemLanguage
    {
        get
        {
            var culture = CultureInfo.CurrentUICulture;
            return string.IsNullOrEmpty(culture.Name) ? CultureInfo.InstalledUICulture.Name : culture.Name;
        }
    }

    /// <summary>Pure resolution used at startup and whenever the setting changes; also unit-testable.</summary>
    public static UiLanguage Resolve(UiLanguage preference, string? systemLanguage) => preference switch
    {
        UiLanguage.English => UiLanguage.English,
        UiLanguage.Arabic => UiLanguage.Arabic,
        _ => IsArabic(systemLanguage) ? UiLanguage.Arabic : UiLanguage.English,
    };

    /// <summary>True for "ar", "ar-SA", "ar_EG"… and the legacy three-letter "ara".</summary>
    public static bool IsArabic(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return false;
        var name = cultureName.Trim();
        var cut = name.IndexOf('-');
        if (cut < 0)
            cut = name.IndexOf('_');
        var primary = cut < 0 ? name : name[..cut];
        return primary.Equals("ar", StringComparison.OrdinalIgnoreCase)
            || primary.Equals("ara", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Applies the saved preference; call once at startup and again on every change.</summary>
    public static void Apply(UiLanguage preference)
    {
        Preference = preference;
        Current = Resolve(preference, SystemLanguage);
        ApplyTextLanguage();
    }

    /// <summary>Direction and pixel snapping for one window, including windows built later.</summary>
    public static void Apply(Window window)
    {
        window.FlowDirection = FlowDirection;
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
    }

    /// <summary>Updates every window that is currently open, without reopening any of them.</summary>
    public static void ApplyToOpenWindows()
    {
        if (Application.Current is null)
            return;
        foreach (Window window in Application.Current.Windows)
            Apply(window);
    }

    /// <summary>
    /// The bridge to <see cref="Loc"/>. Phase 1 switches layout and direction; the Arabic string
    /// tables (a separate R-12 work item) plug in here by using the Arabic member of Loc's language.
    /// </summary>
    private static void ApplyTextLanguage()
    {
        // Loc currently exposes only English and Czech, so English is the safe fallback for Arabic
        // until the string-table migration lands. The direction switch is already live.
        Loc.Current = Language.English;
    }
}
