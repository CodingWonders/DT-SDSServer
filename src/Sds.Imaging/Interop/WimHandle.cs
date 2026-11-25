using Microsoft.Win32.SafeHandles;

namespace Sds.Imaging.Interop;

/// <summary>
/// Safe handle for any handle returned by wimgapi (WIM file handles and image handles).
/// </summary>
/// <remarks>
/// wimgapi requires that image handles are closed before the WIM file handle that owns them.
/// Callers therefore always dispose image handles first, which the using statements in
/// <see cref="WimFile"/> take care of.
/// </remarks>
internal sealed class WimHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>Creates an empty handle; used by the marshaller.</summary>
    public WimHandle() : base(ownsHandle: true)
    {
    }

    /// <summary>Wraps an existing raw handle value.</summary>
    public WimHandle(IntPtr raw) : base(ownsHandle: true)
    {
        SetHandle(raw);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle() => WimNativeMethods.WIMCloseHandle(handle);
}
