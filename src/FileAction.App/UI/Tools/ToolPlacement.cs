using System.Windows;

namespace FileAction.App.UI.Tools;

/// <summary>
/// Places a floating tool next to the pointer instead of in the middle of the monitor. The offset keeps
/// the cursor visible outside the card; when the card would leave the work area it flips to the other
/// side of the pointer and is finally clamped so it can never open partly off-screen.
/// </summary>
internal static class ToolPlacement
{
    public const double CursorOffset = 18;

    public static Point NearCursor(Point cursor, double width, double height, Rect work)
    {
        var left = cursor.X + CursorOffset;
        var top = cursor.Y + CursorOffset;

        if (left + width > work.Right)
            left = cursor.X - width - CursorOffset;
        if (top + height > work.Bottom)
            top = cursor.Y - height - CursorOffset;

        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - width));
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - height));
        return new Point(left, top);
    }
}
