using System.Runtime.InteropServices;
using Sds.Imaging.Interop;

namespace Sds.Imaging;

/// <summary>
/// Managed wrapper around a wimgapi WIM file handle.
/// </summary>
/// <remarks>
/// A <see cref="WimFile"/> must be disposed after every <see cref="WimImage"/> loaded from it.
/// wimgapi calls are synchronous and may take a long time for large images, so callers that
/// run inside a web request should offload them to a background job.
/// </remarks>
public sealed class WimFile : IDisposable
{
    private readonly WimHandle _handle;
    private WimNativeMethods.WimMessageCallback? _callback;
    private IntPtr _callbackPointer;

    private WimFile(WimHandle handle, string path)
    {
        _handle = handle;
        Path = path;
    }

    /// <summary>Full path of the WIM file on disk.</summary>
    public string Path { get; }

    /// <summary>Native handle, used by <see cref="WimImage"/> for export targets.</summary>
    internal WimHandle Handle => _handle;

    /// <summary>Opens an existing WIM (or RWM) file.</summary>
    /// <param name="path">Path of the file.</param>
    /// <param name="writable">True to request write access in addition to read access.</param>
    /// <param name="temporaryPath">Scratch directory wimgapi may use while working.</param>
    public static WimFile Open(string path, bool writable, string temporaryPath)
    {
        uint access = WimNativeMethods.WIM_GENERIC_READ | (writable ? WimNativeMethods.WIM_GENERIC_WRITE : 0);
        return CreateCore(path, access, WimNativeMethods.WIM_OPEN_EXISTING, WimCompression.None, temporaryPath);
    }

    /// <summary>
    /// Creates a new WIM file, or opens it when <paramref name="openIfExists"/> is true and it
    /// already exists. The compression type only applies when the file is created.
    /// </summary>
    public static WimFile Create(string path, WimCompression compression, string temporaryPath, bool openIfExists = false)
    {
        uint disposition = openIfExists ? WimNativeMethods.WIM_OPEN_ALWAYS : WimNativeMethods.WIM_CREATE_NEW;
        uint access = WimNativeMethods.WIM_GENERIC_READ | WimNativeMethods.WIM_GENERIC_WRITE;
        return CreateCore(path, access, disposition, compression, temporaryPath);
    }

    private static WimFile CreateCore(string path, uint access, uint disposition, WimCompression compression, string temporaryPath)
    {
        var raw = WimNativeMethods.WIMCreateFile(path, access, disposition, 0, (uint)compression, out _);
        if (raw == IntPtr.Zero)
        {
            throw WimException.FromLastError($"WIMCreateFile({path})");
        }

        var file = new WimFile(new WimHandle(raw), path);
        try
        {
            // wimgapi refuses most operations until a temporary path has been configured.
            Directory.CreateDirectory(temporaryPath);
            if (!WimNativeMethods.WIMSetTemporaryPath(file._handle, temporaryPath))
            {
                throw WimException.FromLastError("WIMSetTemporaryPath");
            }

            return file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>Number of images stored in the file (Res.RWM files report zero).</summary>
    public int ImageCount => (int)WimNativeMethods.WIMGetImageCount(_handle);

    /// <summary>
    /// Adds a reference (resource) file. Images whose file data lives in another WIM, as is
    /// the case for every WDS metadata WIM that points to Res.RWM, can only be loaded or
    /// exported after the resource file has been referenced.
    /// </summary>
    public void AddReference(string referencePath)
    {
        if (!WimNativeMethods.WIMSetReferenceFile(_handle, referencePath, WimNativeMethods.WIM_REFERENCE_APPEND))
        {
            throw WimException.FromLastError($"WIMSetReferenceFile({referencePath})");
        }
    }

    /// <summary>Returns the raw XML data block of the file (WIM root element).</summary>
    public string GetXml() => WimInfoBuffer.Read(_handle, "WIMGetImageInformation(file)");

    /// <summary>Parses the XML data block into one <see cref="WimImageInfo"/> per image.</summary>
    public IReadOnlyList<WimImageInfo> GetImages()
    {
        // An empty resource only file returns a WIM element without IMAGE children.
        var xml = GetXml();
        return string.IsNullOrWhiteSpace(xml) ? [] : WimXml.ParseWim(xml);
    }

    /// <summary>Loads the image with the given 1 based index.</summary>
    public WimImage LoadImage(int index)
    {
        var raw = WimNativeMethods.WIMLoadImage(_handle, (uint)index);
        if (raw == IntPtr.Zero)
        {
            throw WimException.FromLastError($"WIMLoadImage({Path}, {index})");
        }

        return new WimImage(new WimHandle(raw), index);
    }

    /// <summary>
    /// Captures a directory tree into a new image appended to this file.
    /// Used by tests and by tooling; WDS itself never captures through the server.
    /// </summary>
    public WimImage Capture(string sourceDirectory)
    {
        var raw = WimNativeMethods.WIMCaptureImage(_handle, sourceDirectory, 0);
        if (raw == IntPtr.Zero)
        {
            throw WimException.FromLastError($"WIMCaptureImage({sourceDirectory})");
        }

        return new WimImage(new WimHandle(raw), ImageCount);
    }

    /// <summary>Deletes an image from this file (metadata only, resources stay until rebuilt).</summary>
    public void DeleteImage(int index)
    {
        if (!WimNativeMethods.WIMDeleteImage(_handle, (uint)index))
        {
            throw WimException.FromLastError($"WIMDeleteImage({index})");
        }
    }

    /// <summary>
    /// Registers a progress sink. wimgapi sends WIM_MSG_PROGRESS with the completed
    /// percentage in wParam for every long running operation on this handle.
    /// Only one sink can be registered per <see cref="WimFile"/> instance.
    /// </summary>
    public void RegisterProgress(IProgress<int> progress)
    {
        if (_callback is not null)
        {
            return;
        }

        // Keep a reference to the delegate in a field so the GC cannot collect it while
        // native code still holds the function pointer.
        _callback = (messageId, wParam, _, _) =>
        {
            if (messageId == WimNativeMethods.WIM_MSG_PROGRESS)
            {
                progress.Report((int)wParam);
            }

            return WimNativeMethods.WIM_MSG_SUCCESS;
        };
        _callbackPointer = Marshal.GetFunctionPointerForDelegate(_callback);
        if (WimNativeMethods.WIMRegisterMessageCallback(_handle, _callbackPointer, IntPtr.Zero) == WimNativeMethods.INVALID_CALLBACK_VALUE)
        {
            _callback = null;
            _callbackPointer = IntPtr.Zero;
            throw WimException.FromLastError("WIMRegisterMessageCallback");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_callbackPointer != IntPtr.Zero && !_handle.IsClosed)
        {
            WimNativeMethods.WIMUnregisterMessageCallback(_handle, _callbackPointer);
            _callbackPointer = IntPtr.Zero;
        }

        _handle.Dispose();
        _callback = null;
    }
}

/// <summary>
/// Managed wrapper around a wimgapi image handle (one image inside a WIM file).
/// </summary>
public sealed class WimImage : IDisposable
{
    private readonly WimHandle _handle;

    internal WimImage(WimHandle handle, int index)
    {
        _handle = handle;
        Index = index;
    }

    /// <summary>1 based index of the image in its source file.</summary>
    public int Index { get; }

    /// <summary>Returns the IMAGE XML element of this image.</summary>
    public string GetXml() => WimInfoBuffer.Read(_handle, "WIMGetImageInformation(image)");

    /// <summary>Parses the IMAGE XML element.</summary>
    public WimImageInfo GetInfo() => WimXml.ParseImage(GetXml()) with { Index = Index };

    /// <summary>
    /// Replaces the IMAGE XML element. The owning WIM must have been opened writable;
    /// the change is committed when the WIM handle is closed.
    /// </summary>
    public void SetXml(string imageXml) => WimInfoBuffer.Write(_handle, imageXml);

    /// <summary>Exports this image into <paramref name="destination"/> using the given mode.</summary>
    public void ExportTo(WimFile destination, WimExportMode mode, bool allowDuplicates = true)
    {
        uint flags = mode switch
        {
            WimExportMode.ResourcesOnly => WimNativeMethods.WIM_EXPORT_ONLY_RESOURCES,
            WimExportMode.MetadataOnly => WimNativeMethods.WIM_EXPORT_ONLY_METADATA,
            _ => 0u,
        };

        if (allowDuplicates)
        {
            flags |= WimNativeMethods.WIM_EXPORT_ALLOW_DUPLICATES;
        }

        if (!WimNativeMethods.WIMExportImage(_handle, destination.Handle, flags))
        {
            throw WimException.FromLastError($"WIMExportImage({mode} to {destination.Path})");
        }
    }

    /// <summary>Applies (extracts) the image to a directory. Requires administrative rights.</summary>
    public void Apply(string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        if (!WimNativeMethods.WIMApplyImage(_handle, targetDirectory, 0))
        {
            throw WimException.FromLastError($"WIMApplyImage({targetDirectory})");
        }
    }

    /// <inheritdoc />
    public void Dispose() => _handle.Dispose();
}

/// <summary>How <see cref="WimImage.ExportTo"/> copies the image.</summary>
public enum WimExportMode
{
    /// <summary>Metadata and file resources: produces a standalone, self contained image.</summary>
    Full,

    /// <summary>Only the file resources (WDS: append to Res.RWM).</summary>
    ResourcesOnly,

    /// <summary>Only the metadata resource (WDS: the per image .wim file pointing to Res.RWM).</summary>
    MetadataOnly,
}

/// <summary>
/// Reads and writes the UTF-16 XML buffers exchanged with WIMGetImageInformation and
/// WIMSetImageInformation. Both functions expect or return text starting with a byte order mark.
/// </summary>
internal static class WimInfoBuffer
{
    public static string Read(WimHandle handle, string operation)
    {
        if (!WimNativeMethods.WIMGetImageInformation(handle, out var buffer, out var size))
        {
            throw WimException.FromLastError(operation);
        }

        try
        {
            var text = Marshal.PtrToStringUni(buffer, (int)(size / 2)) ?? string.Empty;
            return text.TrimStart('﻿').TrimEnd('\0');
        }
        finally
        {
            WimNativeMethods.LocalFree(buffer);
        }
    }

    public static void Write(WimHandle handle, string xml)
    {
        // Prefix with the UTF-16 BOM as required by wimgapi.
        var text = "﻿" + xml;
        var buffer = Marshal.StringToHGlobalUni(text);
        try
        {
            if (!WimNativeMethods.WIMSetImageInformation(handle, buffer, (uint)(text.Length * 2)))
            {
                throw WimException.FromLastError("WIMSetImageInformation");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
