using System.Runtime.InteropServices;

namespace Sds.Imaging.Interop;

/// <summary>
/// Raw P/Invoke declarations for the Windows Imaging API (wimgapi.dll).
/// </summary>
/// <remarks>
/// wimgapi.dll ships with every supported Windows Server release (System32). It is the very
/// same library WDS uses to split install images into a shared resource file (Res.RWM) and
/// metadata only WIM files, so using it directly guarantees that SDS produces files with the
/// exact same on-disk layout and format as WDS.
/// Constant values are taken from the public wimgapi.h header of the Windows ADK.
/// </remarks>
internal static partial class WimNativeMethods
{
    private const string Lib = "wimgapi.dll";

    // Desired access flags passed to WIMCreateFile.
    internal const uint WIM_GENERIC_READ = 0x80000000;
    internal const uint WIM_GENERIC_WRITE = 0x40000000;
    internal const uint WIM_GENERIC_MOUNT = 0x20000000;

    // Creation disposition values passed to WIMCreateFile.
    internal const uint WIM_CREATE_NEW = 1;
    internal const uint WIM_CREATE_ALWAYS = 2;
    internal const uint WIM_OPEN_EXISTING = 3;
    internal const uint WIM_OPEN_ALWAYS = 4;

    // Flags for WIMCreateFile.
    internal const uint WIM_FLAG_VERIFY = 0x00000002;
    internal const uint WIM_FLAG_SHARE_WRITE = 0x00000040;

    // Flags for WIMExportImage. ONLY_RESOURCES and ONLY_METADATA are the two halves of the
    // WDS split: the first one appends file data to Res.RWM, the second one writes only the
    // directory tree (metadata resource) into the per image WIM file.
    internal const uint WIM_EXPORT_ALLOW_DUPLICATES = 0x00000001;
    internal const uint WIM_EXPORT_ONLY_RESOURCES = 0x00000002;
    internal const uint WIM_EXPORT_ONLY_METADATA = 0x00000004;
    internal const uint WIM_EXPORT_VERIFY_SOURCE = 0x00000008;
    internal const uint WIM_EXPORT_VERIFY_DESTINATION = 0x00000010;

    // Flags for WIMSetReferenceFile.
    internal const uint WIM_REFERENCE_APPEND = 0x00010000;
    internal const uint WIM_REFERENCE_REPLACE = 0x00020000;

    // Flags for WIMApplyImage (used by tests and diagnostics only).
    internal const uint WIM_FLAG_NO_APPLY = 0x00000008;
    internal const uint WIM_FLAG_INDEX = 0x00000004;

    // Message callback identifiers. WIM_MSG is WM_APP (0x8000) + 0x1476.
    internal const uint WIM_MSG = 0x8000 + 0x1476;
    internal const uint WIM_MSG_PROGRESS = WIM_MSG + 1;
    internal const uint WIM_MSG_ERROR = WIM_MSG + 8;
    internal const uint WIM_MSG_SUCCESS = 0;
    internal const uint INVALID_CALLBACK_VALUE = 0xFFFFFFFF;

    /// <summary>Signature of the callback registered through WIMRegisterMessageCallback.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate uint WimMessageCallback(uint messageId, IntPtr wParam, IntPtr lParam, IntPtr userData);

    [LibraryImport(Lib, EntryPoint = "WIMCreateFile", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr WIMCreateFile(string path, uint desiredAccess, uint creationDisposition, uint flagsAndAttributes, uint compressionType, out uint creationResult);

    [LibraryImport(Lib, EntryPoint = "WIMCloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMCloseHandle(IntPtr handle);

    [LibraryImport(Lib, EntryPoint = "WIMSetTemporaryPath", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMSetTemporaryPath(WimHandle wim, string path);

    [LibraryImport(Lib, EntryPoint = "WIMSetReferenceFile", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMSetReferenceFile(WimHandle wim, string path, uint flags);

    [LibraryImport(Lib, EntryPoint = "WIMGetImageCount", SetLastError = true)]
    internal static partial uint WIMGetImageCount(WimHandle wim);

    [LibraryImport(Lib, EntryPoint = "WIMLoadImage", SetLastError = true)]
    internal static partial IntPtr WIMLoadImage(WimHandle wim, uint imageIndex);

    [LibraryImport(Lib, EntryPoint = "WIMExportImage", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMExportImage(WimHandle image, WimHandle destinationWim, uint flags);

    [LibraryImport(Lib, EntryPoint = "WIMCaptureImage", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr WIMCaptureImage(WimHandle wim, string path, uint captureFlags);

    [LibraryImport(Lib, EntryPoint = "WIMApplyImage", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMApplyImage(WimHandle image, string? path, uint applyFlags);

    [LibraryImport(Lib, EntryPoint = "WIMDeleteImage", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMDeleteImage(WimHandle wim, uint imageIndex);

    [LibraryImport(Lib, EntryPoint = "WIMGetImageInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMGetImageInformation(WimHandle handle, out IntPtr imageInfo, out uint imageInfoSize);

    [LibraryImport(Lib, EntryPoint = "WIMSetImageInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMSetImageInformation(WimHandle handle, IntPtr imageInfo, uint imageInfoSize);

    [LibraryImport(Lib, EntryPoint = "WIMRegisterMessageCallback", SetLastError = true)]
    internal static partial uint WIMRegisterMessageCallback(WimHandle wim, IntPtr callback, IntPtr userData);

    [LibraryImport(Lib, EntryPoint = "WIMUnregisterMessageCallback", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMUnregisterMessageCallback(WimHandle wim, IntPtr callback);

    /// <summary>Frees buffers returned by WIMGetImageInformation.</summary>
    [LibraryImport("kernel32.dll", EntryPoint = "LocalFree")]
    internal static partial IntPtr LocalFree(IntPtr memory);
}
