using System.Windows;

namespace FileAction.App.UI;

/// <summary>
/// The shared visual scale for every DropWheel surface. Tool windows, settings, dialogs and the wheel
/// all read these tokens instead of inventing a nearby value, so one role keeps one size.
/// </summary>
internal static class Design
{
    /// <summary>Type ramp. Roles, not point sizes: a title is a title on every window.</summary>
    internal static class Type
    {
        public const double Display = 20;
        public const double Title = 16;
        public const double Heading = 15;
        public const double Body = 13.5;
        public const double Label = 12.5;
        public const double Caption = 11.5;
        public const double Micro = 10.5;
    }

    /// <summary>4-DIP spacing scale; the values in between the old literals were noise.</summary>
    internal static class Space
    {
        public const double Xs = 2;
        public const double Sm = 4;
        public const double Md = 8;
        public const double Lg = 12;
        public const double Xl = 16;
        public const double Xxl = 18;
        public const double Xxxl = 26;
    }

    internal static class Radius
    {
        public const double Window = 20;
        public const double Card = 12;
        public const double Control = 8;
        public const double Pill = 7;
    }

    /// <summary>Segoe Fluent Icons sizes: inline with text, a control glyph, a section header.</summary>
    internal static class Icon
    {
        public const double Inline = 12;
        public const double Control = 16;
        public const double Header = 18;
    }

    internal static class Opacity
    {
        public const double Disabled = 0.45;
        public const double Hint = 0.85;
    }

    /// <summary>Standard card inset. Compact cards keep the same rhythm at a smaller footprint.</summary>
    internal static Thickness CardPadding => new(Space.Xl, Space.Lg, Space.Xl, Space.Lg);

    internal static Thickness CompactCardPadding => new(Space.Lg, Space.Md, Space.Lg, Space.Md);

    /// <summary>The single horizontal inset shared by the title bar, the content and the action bar.</summary>
    public const double Gutter = Space.Xxl;
}
