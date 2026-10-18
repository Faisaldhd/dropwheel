using System.Runtime.InteropServices;

namespace FileAction.App.Native;

/// <summary>
/// The raw WinRT ABI pieces the app needs: HSTRINGs, activation factories, vtable calls and
/// <c>IAsyncOperation&lt;T&gt;</c> completion. Everything here is classic COM interop
/// (<see cref="Marshal"/>, <c>[ComImport]</c>) plus a few combase.dll entry points, so the file also
/// compiles for plain <c>net10.0-windows</c>, where the Windows SDK projection is not referenced.
/// </summary>
/// <remarks>
/// WinRT interfaces derive from <c>IInspectable</c>; their ABI vtable is IUnknown (slots 0-2),
/// IInspectable (slots 3-5: GetIids, GetRuntimeClassName, GetTrustLevel) and then the interface's own
/// members. All the slot numbers and IIDs below were read from the Windows SDK metadata
/// (Windows.Foundation.UniversalApiContract.winmd / Windows.Foundation.FoundationContract.winmd,
/// 10.0.19041.57).
/// </remarks>
internal static class WinRt
{
    internal static readonly Guid IID_IAsyncInfo = new("00000036-0000-0000-c000-000000000046");
    internal static readonly Guid IID_IStorageFileStatics = new("5984c710-daf2-43c8-8bb4-a4d3eacfd03f");
    internal static readonly Guid IID_IStorageFile = new("fa3f6186-4214-428c-a64c-14c9ac7315ea");
    internal static readonly Guid IID_IStorageItem = new("4207a996-ca2f-42f7-bde8-8b10457a7f30");

    /// <summary>E_ABORT: an <c>IAsyncOperation</c> that was cancelled reports this from GetResults.</summary>
    internal const int EAbort = unchecked((int)0x80004004);

    /// <summary>E_BOUNDS: returned by <c>IVectorView&lt;T&gt;.GetAt</c> for an out-of-range index.</summary>
    internal const int EBounds = unchecked((int)0x8000000B);

    private const uint RO_INIT_MULTITHREADED = 1;
    private const int RpcChangedMode = unchecked((int)0x80010106);

    [ThreadStatic]
    private static bool _comReady;

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoInitialize(uint initType);


    [DllImport("combase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WindowsCreateString(string sourceString, uint length, out nint hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern nint WindowsGetStringRawBuffer(nint hstring, out uint length);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(nint activatableClassId, ref Guid iid, out nint factory);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoActivateInstance(nint activatableClassId, out nint instance);

    /// <summary>
    /// Makes sure the current thread has a COM apartment that WinRT can use. The OCR work runs on
    /// thread-pool threads, which may not have been initialized by anyone else.
    /// </summary>
    /// <remarks>
    /// The initialization is intentionally never undone: cached COM objects (the OCR language vector)
    /// must not outlive the apartment that created them, and this matches what CsWinRT does. The flag
    /// is per thread, so repeated calls do not pile up initialization counts.
    /// </remarks>
    internal static void EnsureCom()
    {
        if (_comReady)
            return;
        var hr = RoInitialize(RO_INIT_MULTITHREADED);
        // S_OK/S_FALSE: initialized (or already initialized in the same mode).
        // RPC_E_CHANGED_MODE: the thread is an STA (the WPF UI thread); WinRT activation works there too.
        _comReady = hr >= 0 || hr == RpcChangedMode;
    }

    internal static nint CreateString(string value)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(value, (uint)value.Length, out var hstring));
        return hstring;
    }

    internal static string? ReadString(nint hstring)
    {
        if (hstring == 0)
            return null;
        var buffer = WindowsGetStringRawBuffer(hstring, out var length);
        return buffer == 0 ? null : Marshal.PtrToStringUni(buffer, (int)length);
    }

    internal static void DeleteString(nint hstring)
    {
        if (hstring != 0)
            WindowsDeleteString(hstring);
    }

    internal static nint ActivationFactory(string className, Guid iid)
    {
        var name = CreateString(className);
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref iid, out var factory));
            return factory;
        }
        finally
        {
            DeleteString(name);
        }
    }

    internal static nint Activate(string className)
    {
        var name = CreateString(className);
        try
        {
            Marshal.ThrowExceptionForHR(RoActivateInstance(name, out var instance));
            return instance;
        }
        finally
        {
            DeleteString(name);
        }
    }

    internal static nint QueryInterface(nint instance, Guid iid)
    {
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(instance, ref iid, out var result));
        return result;
    }

    /// <summary>Queries <paramref name="iid"/> and releases <paramref name="instance"/>, even on failure.</summary>
    internal static nint TakeInterface(nint instance, Guid iid)
    {
        try
        {
            return QueryInterface(instance, iid);
        }
        finally
        {
            Marshal.Release(instance);
        }
    }

    /// <summary>Binds the function pointer in <paramref name="slot"/> of an object's vtable.</summary>
    internal static T Method<T>(nint instance, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        var address = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    internal static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    /// <summary>Releases an <c>IAsyncOperation&lt;T&gt;</c> and returns its result pointer (owned by the caller).</summary>
    internal static nint CallAndWait(nint operation, CancellationToken cancellationToken = default)
    {
        try
        {
            return Await(operation, cancellationToken);
        }
        finally
        {
            Marshal.Release(operation);
        }
    }

    /// <summary>
    /// Waits for an <c>IAsyncOperation&lt;T&gt;</c> and returns its result pointer (owned by the caller).
    /// The operation is not released here.
    /// </summary>
    /// <remarks>
    /// Registers a managed <c>IAsyncOperationCompletedHandler&lt;T&gt;</c> through <c>put_Completed</c>
    /// (vtable slot 7) and waits for the callback. If the handler cannot be registered, or if the callback
    /// has not arrived within 100 ms (for example because the operation lives in another COM apartment),
    /// it polls <c>IAsyncInfo.Status</c> (slot 7) until the operation finishes. Both paths end in
    /// <c>GetResults</c> (slot 8), which also surfaces a failed or cancelled operation as its HRESULT.
    /// </remarks>
    internal static nint Await(nint operation, CancellationToken cancellationToken = default)
    {
        var handler = new WinRtAsyncCompletedHandler();
        nint handlerPointer = 0;
        var registered = false;
        try
        {
            handlerPointer = Marshal.GetComInterfaceForObject(handler, typeof(IWinRtAsyncCompletedHandler));
            var putCompleted = Method<PutCompletedDelegate>(operation, 7);
            registered = putCompleted(operation, handlerPointer) >= 0;
        }
        catch (Exception)
        {
            registered = false;
        }
        finally
        {
            if (handlerPointer != 0)
                Marshal.Release(handlerPointer);
        }

        if (registered)
            handler.Completion.Task.Wait(TimeSpan.FromMilliseconds(100));
        if (!handler.Completion.Task.IsCompleted)
            PollUntilDone(operation, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var getResults = Method<GetResultsDelegate>(operation, 8);
        var hr = getResults(operation, out var result);
        if (hr == EAbort)
            throw new OperationCanceledException(cancellationToken);
        Marshal.ThrowExceptionForHR(hr);
        return result;
    }

    private static void PollUntilDone(nint operation, CancellationToken cancellationToken)
    {
        var info = QueryInterface(operation, IID_IAsyncInfo);
        try
        {
            var getStatus = Method<GetStatusDelegate>(info, 7);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // AsyncStatus.Started == 0; the other values (Completed, Canceled, Error) end the wait.
                if (getStatus(info, out var status) < 0 || status != 0)
                    return;
                Thread.Sleep(2);
            }
        }
        finally
        {
            Marshal.Release(info);
        }
    }


    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GetObjectDelegate(nint instance, out nint value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GetUInt32Delegate(nint instance, out uint value);



    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int SetUInt32Delegate(nint instance, uint value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int SetInt32Delegate(nint instance, int value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int SetStringDelegate(nint instance, nint hstring);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GetAtDelegate(nint instance, uint index, out nint value);



    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int AddHandlerDelegate(nint instance, nint handler, out long token);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int PutCompletedDelegate(nint instance, nint handler);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GetResultsDelegate(nint instance, out nint result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GetStatusDelegate(nint instance, out int status);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int AsyncWithStringDelegate(nint instance, nint hstring, out nint operation);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int AsyncActionDelegate(nint instance, nint argument, out nint operation);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GetObjectWithObjectDelegate(nint instance, nint argument, out nint value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GetForWindowDelegate(nint instance, nint hwnd, ref Guid iid, out nint value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int ShowShareForWindowDelegate(nint instance, nint hwnd);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int SetStorageItemsDelegate(nint instance, nint iterable);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GetSoftwareBitmapDelegate(nint instance, int pixelFormat, int alphaMode, nint transform, int exifMode, int colorManagement, out nint operation);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int OpenAsyncDelegate(nint instance, int accessMode, out nint operation);

}

/// <summary>
/// <c>Windows.Foundation.IAsyncOperationCompletedHandler&lt;T&gt;</c> as seen by the ABI:
/// IInspectable's three members followed by <c>Invoke</c> at vtable slot 6.
/// </summary>
[ComImport]
[Guid("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IWinRtAsyncCompletedHandler
{
    [PreserveSig] int GetIids(out uint iidCount, out nint iids);

    [PreserveSig] int GetRuntimeClassName(out nint className);

    [PreserveSig] int GetTrustLevel(out int trustLevel);

    [PreserveSig] int Invoke(nint asyncInfo, int asyncStatus);
}

/// <summary><c>IInspectable</c>: the three members every WinRT object exposes after IUnknown.</summary>
[ComImport]
[Guid("af86e2e0-b12d-4c6a-9c5a-d7aa65101e90")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IWinRtInspectable
{
    [PreserveSig] int GetIids(out uint iidCount, out nint iids);

    [PreserveSig] int GetRuntimeClassName(out nint className);

    [PreserveSig] int GetTrustLevel(out int trustLevel);
}

/// <summary>
/// <c>IAgileObject</c> marker. Answering its QueryInterface lets WinRT call the managed callback
/// directly from its own thread instead of trying to marshal it into our apartment.
/// </summary>
[ComImport]
[Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IWinRtAgileObject
{
}

/// <summary>Managed COM object handed to <c>IAsyncOperation&lt;T&gt;.put_Completed</c>.</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class WinRtAsyncCompletedHandler : IWinRtAsyncCompletedHandler, IWinRtInspectable, IWinRtAgileObject
{
    internal TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

    public int Invoke(nint asyncInfo, int asyncStatus)
    {
        Completion.TrySetResult(true);
        return 0;
    }
}



