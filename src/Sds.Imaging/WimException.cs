using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Sds.Imaging;

/// <summary>
/// Thrown when a wimgapi call fails. Carries the Win32 error code reported by the API.
/// </summary>
public sealed class WimException : Win32Exception
{
    /// <summary>Creates the exception from an explicit error code.</summary>
    public WimException(string operation, int errorCode)
        : base(errorCode, $"{operation} failed: {new Win32Exception(errorCode).Message} (Win32 error {errorCode}).")
    {
        Operation = operation;
    }

    /// <summary>Name of the wimgapi function or step that failed.</summary>
    public string Operation { get; }

    /// <summary>
    /// Creates the exception from the last Win32 error recorded by the marshaller.
    /// Must be called right after the failing P/Invoke call.
    /// </summary>
    internal static WimException FromLastError(string operation) =>
        new(operation, Marshal.GetLastPInvokeError());
}
