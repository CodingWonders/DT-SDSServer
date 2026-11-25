namespace Sds.Imaging;

/// <summary>
/// Compression types understood by wimgapi. The numeric values match the
/// WIM_COMPRESS_* constants of wimgapi.h and are passed straight to WIMCreateFile.
/// </summary>
public enum WimCompression : uint
{
    /// <summary>No compression (fastest, largest files).</summary>
    None = 0,

    /// <summary>XPRESS compression (fast, moderate ratio).</summary>
    Xpress = 1,

    /// <summary>LZX compression (default of WDS and DISM "maximum").</summary>
    Lzx = 2,

    /// <summary>LZMS compression (solid, used by ESD files; not recommended for Res.RWM).</summary>
    Lzms = 3,
}
