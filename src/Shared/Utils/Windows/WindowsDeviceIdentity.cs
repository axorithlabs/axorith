using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Axorith.Shared.Utils.Windows;

[SupportedOSPlatform("windows10.0.16299")]
internal static class WindowsDeviceIdentity
{
    public static string GetDeviceId()
    {
        var initialized = RoInitialize(1);
        // The Client and installer may already be in an STA apartment.
        if (initialized != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(initialized);

        nint className = 0;
        ISystemIdentificationStatics? factory = null;
        ISystemIdentificationInfo? info = null;
        IBuffer? buffer = null;
        try
        {
            const string runtimeClass = "Windows.System.Profile.SystemIdentification";
            Marshal.ThrowExceptionForHR(WindowsCreateString(runtimeClass, runtimeClass.Length, out className));
            var iid = typeof(ISystemIdentificationStatics).GUID;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, ref iid, out factory));
            info = factory.GetSystemIdForPublisher();
            buffer = info.GetId();
            var length = buffer.GetLength();
            if (info.GetSource() == 0 || length == 0)
                throw new InvalidOperationException("Windows did not provide a system identity.");

            ((IBufferByteAccess)buffer).Buffer(out var bytes);
            var id = new byte[length];
            Marshal.Copy(bytes, id, 0, id.Length);
            // NSIS uses the same lowercase hex encoding of the complete Windows ID.
            return Convert.ToHexStringLower(id);
        }
        finally
        {
            if (buffer is not null) Marshal.ReleaseComObject(buffer);
            if (info is not null) Marshal.ReleaseComObject(info);
            if (factory is not null) Marshal.ReleaseComObject(factory);
            if (className != 0) WindowsDeleteString(className);
            if (initialized >= 0) RoUninitialize();
        }
    }

    [DllImport("combase.dll")]
    private static extern int RoInitialize(uint apartmentType);

    [DllImport("combase.dll")]
    private static extern void RoUninitialize();

    [DllImport("combase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WindowsCreateString(string value, int length, out nint result);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint value);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(nint className, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out ISystemIdentificationStatics factory);

    [ComImport, Guid("5581f42a-d3df-4d93-a37d-c41a616c6d01"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISystemIdentificationStatics
    {
        void GetIids(out uint count, out nint iids);
        void GetRuntimeClassName(out nint name);
        void GetTrustLevel(out int trustLevel);
        ISystemIdentificationInfo GetSystemIdForPublisher();
    }

    [ComImport, Guid("0c659e7d-c3c2-4d33-a2df-21bc41916eb3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISystemIdentificationInfo
    {
        void GetIids(out uint count, out nint iids);
        void GetRuntimeClassName(out nint name);
        void GetTrustLevel(out int trustLevel);
        IBuffer GetId();
        int GetSource();
    }

    [ComImport, Guid("905a0fe0-bc53-11df-8c49-001e4fc686da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBuffer
    {
        void GetIids(out uint count, out nint iids);
        void GetRuntimeClassName(out nint name);
        void GetTrustLevel(out int trustLevel);
        uint GetCapacity();
        uint GetLength();
    }

    [ComImport, Guid("905a0fef-bc53-11df-8c49-001e4fc686da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBufferByteAccess
    {
        void Buffer(out nint bytes);
    }
}
