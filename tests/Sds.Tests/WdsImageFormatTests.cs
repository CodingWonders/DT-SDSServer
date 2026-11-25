using Sds.Imaging;

namespace Sds.Tests;

/// <summary>
/// Integration tests of the WDS split and merge through the real wimgapi.dll.
/// Applying an image requires administrative rights, as WDS itself does.
/// </summary>
public class WdsImageFormatTests : IClassFixture<TestWim>
{
    private readonly TestWim _wim;

    /// <summary>Uses the shared two image WIM.</summary>
    public WdsImageFormatTests(TestWim wim) => _wim = wim;

    [Fact]
    public void Source_images_are_listed()
    {
        var images = WdsImageFormat.ListImages(_wim.WimPath, null, _wim.Temp);
        Assert.Equal(["Test One", "Test Two"], images.Select(i => i.Name));
    }

    [Fact]
    public void Add_splits_into_rwm_and_metadata_with_single_instancing_and_export_merges_back()
    {
        var group = Path.Combine(_wim.Root, "IMAGES", Guid.NewGuid().ToString("N"));

        // Add both images like WDS: file data goes to Res.RWM, metadata to per image WIMs.
        var first = WdsImageFormat.AddImage(_wim.WimPath, 1, group, "install.wim", WimCompression.Lzx, _wim.Temp);
        var rwm = Path.Combine(group, WdsImageFormat.ResourceFileName);
        var afterFirst = new FileInfo(rwm).Length;
        var second = WdsImageFormat.AddImage(_wim.WimPath, 2, group, "install-(2).wim", WimCompression.Lzx, _wim.Temp, "Renamed", "New description");
        var afterSecond = new FileInfo(rwm).Length;

        Assert.Equal("Test One", first.Name);
        Assert.Equal("Renamed", second.Name);
        Assert.Equal("New description", second.Description);

        // The 4 MB shared file is stored once: the second image adds only a few bytes.
        Assert.True(afterFirst > 3_900_000, $"Res.RWM too small: {afterFirst}");
        Assert.True(afterSecond - afterFirst < 64 * 1024, $"Res.RWM grew by {afterSecond - afterFirst} bytes");

        // Metadata WIMs carry no file data.
        Assert.True(new FileInfo(Path.Combine(group, "install-(2).wim")).Length < 64 * 1024);

        // Export back to a standalone WIM and apply it to compare content.
        var exported = Path.Combine(_wim.Root, Guid.NewGuid().ToString("N") + ".wim");
        var info = WdsImageFormat.ExportImage(Path.Combine(group, "install-(2).wim"), rwm, exported, WimCompression.Lzx, _wim.Temp);
        Assert.Equal("Renamed", info.Name);

        var applied = Path.Combine(_wim.Root, Guid.NewGuid().ToString("N"));
        using (var wim = WimFile.Open(exported, writable: false, _wim.Temp))
        using (var image = wim.LoadImage(1))
        {
            image.Apply(applied);
        }

        Assert.Equal("image two", File.ReadAllText(Path.Combine(applied, "two.txt")));
        Assert.Equal(_wim.SharedContent, File.ReadAllBytes(Path.Combine(applied, "shared.bin")));
    }

    [Fact]
    public void Compact_keeps_remaining_images_usable()
    {
        var group = Path.Combine(_wim.Root, "IMAGES", Guid.NewGuid().ToString("N"));
        WdsImageFormat.AddImage(_wim.WimPath, 1, group, "a.wim", WimCompression.Lzx, _wim.Temp);
        WdsImageFormat.AddImage(_wim.WimPath, 2, group, "b.wim", WimCompression.Lzx, _wim.Temp);
        File.Delete(Path.Combine(group, "a.wim"));

        var reclaimed = WdsImageFormat.Compact(group, WimCompression.Lzx, _wim.Temp);
        Assert.True(reclaimed > 0);

        var info = WdsImageFormat.ReadImageInfo(Path.Combine(group, "b.wim"), Path.Combine(group, WdsImageFormat.ResourceFileName), _wim.Temp);
        Assert.Equal("Test Two", info.Name);
        var exported = Path.Combine(_wim.Root, Guid.NewGuid().ToString("N") + ".wim");
        WdsImageFormat.ExportImage(Path.Combine(group, "b.wim"), Path.Combine(group, WdsImageFormat.ResourceFileName), exported, WimCompression.Lzx, _wim.Temp);
        Assert.True(new FileInfo(exported).Length > 3_900_000);
    }
}
