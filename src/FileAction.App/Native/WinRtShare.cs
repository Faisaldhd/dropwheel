using System.Runtime.InteropServices;

namespace FileAction.App.Native;

/// <summary>
/// The Windows share sheet through the raw WinRT ABI: <c>IDataTransferManagerInterop.GetForWindow</c>,
/// <c>IDataTransferManager.DataRequested</c> and a <c>DataPackage</c> filled from managed COM objects.
/// This is the documented Win32 path for unpackaged desktop apps.
/// </summary>
/// <remarks>
/// The share payload is built from three managed COM objects: the <c>DataRequested</c> event handler,
/// and an <c>IIterable&lt;IStorageItem&gt;</c> plus its <c>IIterator&lt;IStorageItem&gt;</c> that carry
/// the files. WinRT receives them as typed interface pointers, so their vtable layout (IUnknown,
/// IInspectable, then the members) is what matters; the GUIDs only have to be stable for our own
/// QueryInterface bookkeeping.
/// </remarks>
internal static class WinRtShare
{
    private const string DataTransferManagerClass = "Windows.ApplicationModel.DataTransfer.DataTransferManager";
    private const string StorageFileClass = "Windows.Storage.StorageFile";

    private static readonly Guid IID_IDataTransferManagerInterop = new("3a3dcd6c-3eab-43dc-bcde-45671ce800c8");
    private static readonly Guid IID_IDataTransferManager = new("a5caee9b-8708-49d1-8d36-67d25a8da00c");
    internal static readonly Guid IID_IDataRequestedEventArgs = new("cb8ba807-6ac5-43c9-8ac5-9ba232163182");
    internal static readonly Guid IID_IDataRequest = new("4341ae3b-fc12-4e53-8c02-ac714c415a27");
    internal static readonly Guid IID_IDataPackage = new("61ebf5c7-efea-4346-9554-981d7e198ffe");
    internal static readonly Guid IID_IDataPackagePropertySet = new("cd1c93eb-4c4c-443a-a8d3-f5c241e91689");

    // The last share stays referenced until the next one: the shell may still be reading the package,
    // and the data objects are small (no file handles are held by StorageFile itself).
    private static nint _manager;
    private static nint _handler;
    private static nint[] _items = [];

    /// <summary>Resolves the files to <c>IStorageItem</c> pointers. Call on a background (MTA) thread.</summary>
    internal static nint[] LoadItems(IReadOnlyList<string> paths)
    {
        WinRt.EnsureCom();
        var items = new nint[paths.Count];
        for (var i = 0; i < items.Length; i++)
            items[i] = LoadItem(paths[i]);
        return items;
    }

    private static nint LoadItem(string path)
    {
        var factory = WinRt.ActivationFactory(StorageFileClass, WinRt.IID_IStorageFileStatics);
        nint operation = 0;
        try
        {
            var hpath = WinRt.CreateString(Path.GetFullPath(path));
            try
            {
                var getFile = WinRt.Method<WinRt.AsyncWithStringDelegate>(factory, 6);
                WinRt.Check(getFile(factory, hpath, out operation));
            }
            finally
            {
                WinRt.DeleteString(hpath);
            }
        }
        finally
        {
            Marshal.Release(factory);
        }

        var fileObject = WinRt.CallAndWait(operation);
        try
        {
            return WinRt.QueryInterface(fileObject, WinRt.IID_IStorageItem);
        }
        finally
        {
            Marshal.Release(fileObject);
        }
    }

    /// <summary>Shows the share sheet for the window. Call on the UI thread.</summary>
    internal static void Show(nint hwnd, nint[] items, string title)
    {
        ReleaseSession();
        WinRt.EnsureCom();

        var handler = new WinRtDataRequestedHandler(title, items);
        var handlerPointer = Marshal.GetComInterfaceForObject(handler, typeof(IWinRtDataRequestedHandler));

        var factory = WinRt.ActivationFactory(DataTransferManagerClass, IID_IDataTransferManagerInterop);
        nint manager = 0;
        try
        {
            var getForWindow = WinRt.Method<WinRt.GetForWindowDelegate>(factory, 3);
            var iid = IID_IDataTransferManager;
            WinRt.Check(getForWindow(factory, hwnd, ref iid, out manager));

            var addHandler = WinRt.Method<WinRt.AddHandlerDelegate>(manager, 6);
            WinRt.Check(addHandler(manager, handlerPointer, out _));

            var show = WinRt.Method<WinRt.ShowShareForWindowDelegate>(factory, 4);
            WinRt.Check(show(factory, hwnd));
        }
        catch
        {
            if (manager != 0)
                Marshal.Release(manager);
            Marshal.Release(handlerPointer);
            throw;
        }
        finally
        {
            Marshal.Release(factory);
        }

        _manager = manager;
        _handler = handlerPointer;
        _items = items;

        GC.KeepAlive(handler);
    }

    private static void ReleaseSession()
    {
        if (_manager != 0)
        {
            Marshal.Release(_manager);
            _manager = 0;
        }
        if (_handler != 0)
        {
            Marshal.Release(_handler);
            _handler = 0;
        }
        foreach (var item in _items)
            Marshal.Release(item);
        _items = [];
    }
}

/// <summary><c>TypedEventHandler&lt;DataTransferManager, DataRequestedEventArgs&gt;</c>.</summary>
[ComImport]
[Guid("5f6a7b8c-9d0e-4f1a-8b2c-3d4e5f6a7b8c")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IWinRtDataRequestedHandler
{
    [PreserveSig] int GetIids(out uint iidCount, out nint iids);

    [PreserveSig] int GetRuntimeClassName(out nint className);

    [PreserveSig] int GetTrustLevel(out int trustLevel);

    [PreserveSig] int Invoke(nint sender, nint args);
}

/// <summary><c>IIterable&lt;IStorageItem&gt;</c>: one member, <c>First</c>.</summary>
[ComImport]
[Guid("6a7b8c9d-0e1f-4a2b-9c3d-4e5f6a7b8c9d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IWinRtIterableOfStorageItem
{
    [PreserveSig] int GetIids(out uint iidCount, out nint iids);

    [PreserveSig] int GetRuntimeClassName(out nint className);

    [PreserveSig] int GetTrustLevel(out int trustLevel);

    [PreserveSig] int First(out nint iterator);
}

/// <summary><c>IIterator&lt;IStorageItem&gt;</c>.</summary>
[ComImport]
[Guid("7b8c9d0e-1f2a-4b3c-8d4e-5f6a7b8c9d0e")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IWinRtIteratorOfStorageItem
{
    [PreserveSig] int GetIids(out uint iidCount, out nint iids);

    [PreserveSig] int GetRuntimeClassName(out nint className);

    [PreserveSig] int GetTrustLevel(out int trustLevel);

    [PreserveSig] int GetCurrent(out nint value);

    [PreserveSig] int GetHasCurrent(out byte value);

    [PreserveSig] int MoveNext(out byte value);

    [PreserveSig] int GetMany(nint items, uint capacity, out uint value);
}

/// <summary>Managed <c>DataRequested</c> handler: fills the <c>DataPackage</c> for each share request.</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class WinRtDataRequestedHandler : IWinRtDataRequestedHandler, IWinRtInspectable, IWinRtAgileObject
{
    private readonly string _title;
    private readonly nint[] _items;

    public WinRtDataRequestedHandler(string title, nint[] items)
    {
        _title = title;
        _items = items;
    }

    public int GetIids(out uint iidCount, out nint iids)
    {
        iidCount = 0;
        iids = 0;
        return 0;
    }

    public int GetRuntimeClassName(out nint className)
    {
        className = 0;
        return 0;
    }

    public int GetTrustLevel(out int trustLevel)
    {
        trustLevel = 0;
        return 0;
    }

    public int Invoke(nint sender, nint args)
    {
        try
        {
            return Populate(args);
        }
        catch (Exception)
        {
            return unchecked((int)0x80004005);
        }
    }

    private int Populate(nint args)
    {
        var eventArgs = WinRt.QueryInterface(args, WinRtShare.IID_IDataRequestedEventArgs);
        try
        {
            var getRequest = WinRt.Method<WinRt.GetObjectDelegate>(eventArgs, 6);
            WinRt.Check(getRequest(eventArgs, out var requestObject));
            var request = WinRt.TakeInterface(requestObject, WinRtShare.IID_IDataRequest);
            try
            {
                var getData = WinRt.Method<WinRt.GetObjectDelegate>(request, 6);
                WinRt.Check(getData(request, out var packageObject));
                var package = WinRt.TakeInterface(packageObject, WinRtShare.IID_IDataPackage);
                try
                {
                    var getProperties = WinRt.Method<WinRt.GetObjectDelegate>(package, 7);
                    WinRt.Check(getProperties(package, out var propertiesObject));
                    var properties = WinRt.TakeInterface(propertiesObject, WinRtShare.IID_IDataPackagePropertySet);
                    try
                    {
                        var title = WinRt.CreateString(_title);
                        try
                        {
                            var putTitle = WinRt.Method<WinRt.SetStringDelegate>(properties, 7);
                            WinRt.Check(putTitle(properties, title));
                        }
                        finally
                        {
                            WinRt.DeleteString(title);
                        }

                        var applicationName = WinRt.CreateString("DropWheel");
                        try
                        {
                            var putApplicationName = WinRt.Method<WinRt.SetStringDelegate>(properties, 14);
                            WinRt.Check(putApplicationName(properties, applicationName));
                        }
                        finally
                        {
                            WinRt.DeleteString(applicationName);
                        }
                    }
                    finally
                    {
                        Marshal.Release(properties);
                    }

                    var iterable = Marshal.GetComInterfaceForObject(new WinRtStorageItemIterable(_items), typeof(IWinRtIterableOfStorageItem));
                    try
                    {
                        var setStorageItems = WinRt.Method<WinRt.SetStorageItemsDelegate>(package, 22);
                        WinRt.Check(setStorageItems(package, iterable));
                    }
                    finally
                    {
                        Marshal.Release(iterable);
                    }
                }
                finally
                {
                    Marshal.Release(package);
                }
            }
            finally
            {
                Marshal.Release(request);
            }
        }
        finally
        {
            Marshal.Release(eventArgs);
        }

        return 0;
    }
}

/// <summary>Managed <c>IIterable&lt;IStorageItem&gt;</c> over the resolved files.</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class WinRtStorageItemIterable : IWinRtIterableOfStorageItem, IWinRtInspectable, IWinRtAgileObject
{
    private readonly nint[] _items;

    public WinRtStorageItemIterable(nint[] items) => _items = items;

    public int GetIids(out uint iidCount, out nint iids)
    {
        iidCount = 0;
        iids = 0;
        return 0;
    }

    public int GetRuntimeClassName(out nint className)
    {
        className = 0;
        return 0;
    }

    public int GetTrustLevel(out int trustLevel)
    {
        trustLevel = 0;
        return 0;
    }

    public int First(out nint iterator)
    {
        try
        {
            iterator = Marshal.GetComInterfaceForObject(new WinRtStorageItemIterator(_items), typeof(IWinRtIteratorOfStorageItem));
            return 0;
        }
        catch (Exception)
        {
            iterator = 0;
            return unchecked((int)0x80004005);
        }
    }
}

/// <summary>Managed <c>IIterator&lt;IStorageItem&gt;</c>; the share target walks the files with it.</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class WinRtStorageItemIterator : IWinRtIteratorOfStorageItem, IWinRtInspectable, IWinRtAgileObject
{
    private readonly nint[] _items;
    private int _index = -1;

    public WinRtStorageItemIterator(nint[] items) => _items = items;

    public int GetIids(out uint iidCount, out nint iids)
    {
        iidCount = 0;
        iids = 0;
        return 0;
    }

    public int GetRuntimeClassName(out nint className)
    {
        className = 0;
        return 0;
    }

    public int GetTrustLevel(out int trustLevel)
    {
        trustLevel = 0;
        return 0;
    }

    public int GetCurrent(out nint value)
    {
        if (_index < 0 || _index >= _items.Length)
        {
            value = 0;
            return WinRt.EBounds;
        }

        value = _items[_index];
        Marshal.AddRef(value);
        return 0;
    }

    public int GetHasCurrent(out byte value)
    {
        value = (byte)(_index >= 0 && _index < _items.Length ? 1 : 0);
        return 0;
    }

    public int MoveNext(out byte value)
    {
        if (_index < _items.Length)
            _index++;
        value = (byte)(_index < _items.Length ? 1 : 0);
        return 0;
    }

    public int GetMany(nint items, uint capacity, out uint value)
    {
        value = 0;
        if (items == 0)
            return 0;

        // IIterator.GetMany has no start index: it fills from the current position, like repeated
        // Current/MoveNext. It is not used by the share sheet today, but the vtable must be correct.
        while (value < capacity && _index + 1 < _items.Length)
        {
            _index++;
            var item = _items[_index];
            Marshal.AddRef(item);
            Marshal.WriteIntPtr(items, (int)value * IntPtr.Size, item);
            value++;
        }
        return 0;
    }
}



