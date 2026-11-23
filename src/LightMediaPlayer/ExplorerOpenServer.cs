using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace LightMediaPlayer;

// Explorer supplies the complete selection as one OLE drop, including when
// the default Open verb is invoked with Enter. Each activation owns a process.
internal sealed class ExplorerOpenServer : IDisposable
{
    private static readonly Guid ClassId = new("35BE37DB-35E8-4327-95F2-104965F7A571");
    private uint _registration;
    private bool _oleInitialized;
    private readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromMinutes(1) };

    public void Start(Action<string[]> open)
    {
        Marshal.ThrowExceptionForHR(OleInitialize(0));
        _oleInitialized = true;
        var factory = new ExplorerOpenFactory(paths =>
        {
            _idle.Stop();
            open(paths);
        });
        // CLSCTX_LOCAL_SERVER, REGCLS_SINGLEUSE: a second Open gets its own factory.
        Marshal.ThrowExceptionForHR(CoRegisterClassObject(in ClassId, factory, 4, 0, out _registration));
        _idle.Tick += (_, _) => Application.Current.Shutdown();
        _idle.Start();
    }

    public void Dispose()
    {
        _idle.Stop();
        if (_registration != 0) CoRevokeClassObject(_registration);
        if (_oleInitialized) OleUninitialize();
    }

    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("ole32.dll")] private static extern int CoRegisterClassObject(in Guid clsid, [MarshalAs(UnmanagedType.IUnknown)] object factory, uint context, uint flags, out uint registration);
    [DllImport("ole32.dll")] private static extern int CoRevokeClassObject(uint registration);
}

[ComVisible(true), Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerClassFactory
{
    [PreserveSig] int CreateInstance(nint outer, in Guid iid, out nint instance);
    [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool locked);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ExplorerOpenFactory(Action<string[]> open) : IExplorerClassFactory
{
    public int CreateInstance(nint outer, in Guid iid, out nint instance)
    {
        instance = 0;
        if (outer != 0) return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION
        var target = Marshal.GetIUnknownForObject(new ExplorerDropTarget(open));
        try { return Marshal.QueryInterface(target, in iid, out instance); }
        finally { Marshal.Release(target); }
    }

    public int LockServer(bool locked) => 0;
}

[ComVisible(true), Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerDropTarget
{
    void DragEnter(System.Runtime.InteropServices.ComTypes.IDataObject data, uint keys, long point, ref uint effect);
    void DragOver(uint keys, long point, ref uint effect);
    void DragLeave();
    void Drop(System.Runtime.InteropServices.ComTypes.IDataObject data, uint keys, long point, ref uint effect);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ExplorerDropTarget(Action<string[]> open) : IExplorerDropTarget
{
    public void DragEnter(System.Runtime.InteropServices.ComTypes.IDataObject data, uint keys, long point, ref uint effect) => effect &= 1;
    public void DragOver(uint keys, long point, ref uint effect) => effect &= 1;
    public void DragLeave() { }

    public void Drop(System.Runtime.InteropServices.ComTypes.IDataObject data, uint keys, long point, ref uint effect)
    {
        var format = new FORMATETC { cfFormat = 15, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
        data.GetData(ref format, out var medium);
        try
        {
            var paths = new string[DragQueryFile(medium.unionmember, uint.MaxValue, null, 0)];
            for (uint i = 0; i < paths.Length; i++)
            {
                var length = DragQueryFile(medium.unionmember, i, null, 0);
                var path = new StringBuilder(checked((int)length + 1));
                DragQueryFile(medium.unionmember, i, path, (uint)path.Capacity);
                paths[i] = path.ToString();
            }
            effect &= 1; // Opening files never moves them.
            if (paths.Length > 0) open(paths);
        }
        finally { ReleaseStgMedium(ref medium); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFile(nint drop, uint index, StringBuilder? path, uint capacity);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
}
