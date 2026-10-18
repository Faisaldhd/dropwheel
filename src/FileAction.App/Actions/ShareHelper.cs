using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FileAction.App.Native;
using FileAction.App.UI;
using FileAction.Core;
#if WINDOWS10_0_19041_0_OR_GREATER
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
#endif
using static FileAction.Core.Loc;

namespace FileAction.App.Actions;

/// <summary>Opens the Windows share sheet (Nearby sharing, Mail, Teams, …) for files.</summary>
/// <remarks>
/// The WinRT-projection build uses <c>DataTransferManagerInterop</c> and the projected
/// <c>StorageFile</c>; the plain <c>net10.0-windows</c> target reaches the same sheet through
/// <see cref="WinRtShare"/> (raw COM). Public behaviour is identical in both builds.
/// </remarks>
internal static class ShareHelper
{
#if WINDOWS10_0_19041_0_OR_GREATER
    private static Window? _host;
    private static IReadOnlyList<IStorageItem> _pending = [];
    private static string _title = "";

    public static async Task ShareAsync(IReadOnlyList<FileItem> items)
    {
        var storageItems = new List<IStorageItem>();
        foreach (var item in items)
            storageItems.Add(await StorageFile.GetFileFromPathAsync(item.Path));
        _pending = storageItems;
        _title = items.Count == 1 ? items[0].Name : Items(items.Count);

        var hwnd = EnsureHost();
        DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
    }

    /// <summary>The share sheet needs an owner window; a 1×1 invisible one near the cursor is enough.</summary>
    private static IntPtr EnsureHost()
    {
        if (_host is null)
        {
            _host = new Window
            {
                Width = 1,
                Height = 1,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize,
                Title = "DropWheel",
            };
            _host.Show();
            var handle = new WindowInteropHelper(_host).Handle;
            var manager = DataTransferManagerInterop.GetForWindow(handle);
            manager.DataRequested += (_, e) =>
            {
                var request = e.Request;
                request.Data.Properties.Title = _title;
                request.Data.Properties.ApplicationName = "DropWheel";
                request.Data.SetStorageItems(_pending);
            };
        }
        var hwnd = new WindowInteropHelper(_host).Handle;
        Win32.GetCursorPos(out var p);
        Win32.SetWindowPos(hwnd, IntPtr.Zero, p.X, p.Y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER);
        ForegroundHelper.Force(_host);
        return hwnd;
    }
#else
    private static Window? _host;

    public static async Task ShareAsync(IReadOnlyList<FileItem> items)
    {
        var title = items.Count == 1 ? items[0].Name : Items(items.Count);
        var paths = items.Select(i => i.Path).ToArray();

        // The raw-COM wait blocks, so resolve the files on a background (MTA) thread; the share UI
        // itself is opened from this (UI) thread, where the owner window lives.
        var storageItems = await Task.Run(() => WinRtShare.LoadItems(paths)).ConfigureAwait(true);
        var hwnd = EnsureHost();
        WinRtShare.Show(hwnd, storageItems, title);
    }

    /// <summary>The share sheet needs an owner window; a 1×1 invisible one near the cursor is enough.</summary>
    private static IntPtr EnsureHost()
    {
        if (_host is null)
        {
            _host = new Window
            {
                Width = 1,
                Height = 1,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize,
                Title = "DropWheel",
            };
            _host.Show();
        }
        var hwnd = new WindowInteropHelper(_host).Handle;
        Win32.GetCursorPos(out var p);
        Win32.SetWindowPos(hwnd, IntPtr.Zero, p.X, p.Y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER);
        ForegroundHelper.Force(_host);
        return hwnd;
    }
#endif
}
