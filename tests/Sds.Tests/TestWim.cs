using Sds.Imaging;

namespace Sds.Tests;

/// <summary>
/// Builds a small two image WIM for tests. Both images share a 4 MB file, which lets the
/// tests verify single instance storage in Res.RWM.
/// </summary>
public sealed class TestWim : IDisposable
{
    /// <summary>Creates the folders and the WIM.</summary>
    public TestWim()
    {
        Root = Path.Combine(Path.GetTempPath(), "sds-tests", Guid.NewGuid().ToString("N"));
        Temp = Path.Combine(Root, "tmp");
        SharedContent = new byte[4_000_000];
        new Random(42).NextBytes(SharedContent);

        var one = Path.Combine(Root, "src1");
        var two = Path.Combine(Root, "src2");
        Directory.CreateDirectory(one);
        Directory.CreateDirectory(two);
        File.WriteAllBytes(Path.Combine(one, "shared.bin"), SharedContent);
        File.WriteAllBytes(Path.Combine(two, "shared.bin"), SharedContent);
        File.WriteAllText(Path.Combine(one, "one.txt"), "image one");
        File.WriteAllText(Path.Combine(two, "two.txt"), "image two");

        WimPath = Path.Combine(Root, "install.wim");
        using var wim = WimFile.Create(WimPath, WimCompression.Lzx, Temp);
        using (var image = wim.Capture(one))
        {
            image.SetXml(WimXml.UpdateImageText(image.GetXml(), "Test One", "First test image"));
        }

        using (var image = wim.Capture(two))
        {
            image.SetXml(WimXml.UpdateImageText(image.GetXml(), "Test Two", "Second test image"));
        }
    }

    /// <summary>Root folder of the fixture.</summary>
    public string Root { get; }

    /// <summary>wimgapi scratch folder.</summary>
    public string Temp { get; }

    /// <summary>Path of the two image source WIM.</summary>
    public string WimPath { get; }

    /// <summary>Content of shared.bin in both images.</summary>
    public byte[] SharedContent { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Files may still be locked by a handle closing in the background.
        }
    }
}
