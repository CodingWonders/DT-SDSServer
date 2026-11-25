using Sds.Core;
using Sds.Core.Storage;

namespace Sds.Server.Infrastructure;

/// <summary>
/// Stores files uploaded through POST /api/uploads in Staging\uploads\&lt;id&gt;\&lt;name&gt;, so that
/// image and driver operations can use them like a server side path.
/// </summary>
public sealed class UploadStore(ShareLayout layout)
{
    private string Root => Path.Combine(layout.Staging, "uploads");

    /// <summary>Saves a request body stream; returns the upload id.</summary>
    public async Task<(string Id, string Path, long Size)> SaveAsync(Stream body, string fileName, CancellationToken ct)
    {
        var name = ShareLayout.ValidateName(Path.GetFileName(fileName), "File name");
        var id = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(Root, id);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);

        // Large WIM files: stream straight to disk with a big buffer, never buffer in memory.
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
        {
            await body.CopyToAsync(file, 1024 * 1024, ct);
        }

        return (id, path, new FileInfo(path).Length);
    }

    /// <summary>Resolves an upload id to the stored file path.</summary>
    public string Resolve(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
        {
            throw new SdsValidationException("Invalid upload id.");
        }

        var dir = Path.Combine(Root, id);
        var file = Directory.Exists(dir) ? Directory.GetFiles(dir).FirstOrDefault() : null;
        return file ?? throw new SdsNotFoundException($"Upload {id} not found or expired.");
    }

    /// <summary>Deletes an upload.</summary>
    public void Delete(string id)
    {
        var dir = Path.Combine(Root, id);
        if (Guid.TryParseExact(id, "N", out _) && Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
