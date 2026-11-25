using Sds.Core;
using Sds.Core.Computers;
using Sds.Core.Drivers;
using Sds.Core.Storage;
using Sds.Core.Util;
using Sds.Data.Entities;

namespace Sds.Tests;

/// <summary>Unit tests of the UNC mapping: IMAGES and DRIVERS are separate shares.</summary>
public class ShareLayoutUncTests
{
    private static ShareLayout Layout() => new(Microsoft.Extensions.Options.Options.Create(new SdsOptions
    {
        RootOverride = @"E:\SDSREMINST",
        PublicServerName = "sds01",
    }));

    /// <summary>Image files map to \\host\images without the IMAGES folder name.</summary>
    [Fact]
    public void Maps_images_to_the_images_share()
    {
        var layout = Layout();
        Assert.Equal(@"\\sds01\images", layout.ToUnc(layout.Images));
        Assert.Equal(@"\\sds01\images\Win11\install.wim", layout.ToUnc(layout.ImageFile("Win11", "install.wim")));
    }

    /// <summary>Driver folders map to \\host\drivers.</summary>
    [Fact]
    public void Maps_drivers_to_the_drivers_share()
    {
        var layout = Layout();
        Assert.Equal(@"\\sds01\drivers\e1d_x64_{0}", layout.ToUnc(layout.DriverDirectory("e1d_x64_{0}")));
    }

    /// <summary>The root and PXECORE are not shared, so they have no UNC path.</summary>
    [Fact]
    public void Rejects_paths_outside_the_shared_folders()
    {
        var layout = Layout();
        Assert.Throws<InvalidOperationException>(() => layout.ToUnc(layout.PxeCore));
        Assert.Throws<InvalidOperationException>(() => layout.ToUnc(layout.Root));
        Assert.Throws<InvalidOperationException>(() => layout.ToUnc(@"E:\SDSREMINST\IMAGESX\a.wim"));
    }
}

/// <summary>Unit tests of the INF parser.</summary>
public class InfParserTests
{
    /// <summary>A small but realistic multi architecture network driver INF.</summary>
    public const string SampleInf = """
        ; Sample network driver
        [Version]
        Signature   = "$WINDOWS NT$"
        Class       = Net
        ClassGuid   = {4d36e972-e325-11ce-bfc1-08002be10318}
        Provider    = %ProviderName%
        DriverVer   = 03/15/2024,12.19.1.37
        CatalogFile = sample.cat

        [Manufacturer]
        %Mfg% = Sample, NTamd64.10.0...16299, NTarm64, NTx86

        [Sample.NTamd64.10.0...16299]
        %Dev1% = Install, PCI\VEN_8086&DEV_15B8, PCI\VEN_8086&CC_0200 ; comment
        %Dev2% = Install, PCI\VEN_8086&DEV_15BB

        [Sample.NTarm64]
        %Dev1% = Install, PCI\VEN_8086&DEV_15B8

        [Sample.NTx86]
        "Literal ""quoted"" name" = Install, \
            PCI\VEN_8086&DEV_1000

        [SourceDisksNames]
        1 = %Disk%,,,\files

        [SourceDisksFiles]
        sample.sys = 1
        helper.dll = 1,sub

        [Strings]
        ProviderName = "Contoso; Ltd"
        Mfg = "Contoso"
        Dev1 = "Contoso Gigabit"
        Dev2 = "Contoso 10G"
        Disk = "Disk 1"
        """;

    [Fact]
    public void Parses_version_section_and_strings()
    {
        var info = InfParser.Parse(SampleInf);
        Assert.Equal("Net", info.ClassName);
        Assert.Equal(Guid.Parse("4d36e972-e325-11ce-bfc1-08002be10318"), info.ClassGuid);
        Assert.Equal("Contoso; Ltd", info.Provider); // ';' inside quotes is not a comment.
        Assert.Equal(new DateOnly(2024, 3, 15), info.DriverDate);
        Assert.Equal("12.19.1.37", info.Version);
        Assert.Equal("sample.cat", info.CatalogFor("amd64"));
    }

    [Fact]
    public void Groups_models_per_architecture_with_decorations()
    {
        var info = InfParser.Parse(SampleInf);
        Assert.Equal(["amd64", "arm64", "x86"], info.ModelsByArchitecture.Keys.Order().ToArray());

        var amd64 = info.ModelsByArchitecture["amd64"];
        Assert.Equal(2, amd64.Count);
        Assert.Equal("Contoso Gigabit", amd64[0].Description);
        Assert.Equal(["PCI\\VEN_8086&DEV_15B8", "PCI\\VEN_8086&CC_0200"], amd64[0].HardwareIds);

        // Line continuation and quoted literal key.
        var x86 = Assert.Single(info.ModelsByArchitecture["x86"]);
        Assert.Equal("Literal \"quoted\" name", x86.Description);
        Assert.Equal("PCI\\VEN_8086&DEV_1000", Assert.Single(x86.HardwareIds));
    }

    [Fact]
    public void Resolves_source_files_with_disk_paths()
    {
        var files = InfParser.Parse(SampleInf).FilesByArchitecture["amd64"].Select(f => f.RelativePath).ToList();
        Assert.Contains(Path.Combine("files", "sample.sys"), files);
        Assert.Contains(Path.Combine("files", "sub", "helper.dll"), files);
    }

    [Theory]
    [InlineData("NTamd64", "amd64")]
    [InlineData("ntAMD64.10.0...19041", "amd64")]
    [InlineData("NTx86.6.1", "x86")]
    [InlineData("NTarm64", "arm64")]
    [InlineData("NT", null)]
    public void Maps_decorations(string decoration, string? expected) =>
        Assert.Equal(expected, InfParser.ArchitectureFromDecoration(decoration));
}

/// <summary>Unit tests of the WDS naming policy.</summary>
public class NamingPolicyTests
{
    private static Task<string> Generate(string policy, NamingValues values, params string[] taken) =>
        NamingPolicy.GenerateAsync(policy, values, n => Task.FromResult(taken.Contains(n)));

    [Fact]
    public async Task Default_policy_uses_username_and_counter()
    {
        Assert.Equal("JDOE1", await Generate("%61Username%#", new NamingValues(null, null, @"CONTOSO\jdoe", null)));
        Assert.Equal("JDOE3", await Generate("%61Username%#", new NamingValues(null, null, "jdoe@contoso.com", null), "JDOE1", "JDOE2"));
    }

    [Fact]
    public async Task Supports_length_limits_padding_and_literals()
    {
        var values = new NamingValues("Alice", "Smith", "asmith", "001122AABBCC");
        Assert.Equal("ASM-001", await Generate("%1First%2Last-%03#", values));
        Assert.Equal("PC-AABBCC", await Generate("PC-%MAC", values with { Mac = "AABBCC" }));
        Assert.Equal("LAB-100", await Generate("LAB-%#", values, Enumerable.Range(1, 99).Select(i => "LAB-" + i).ToArray()));
    }

    [Fact]
    public async Task Truncates_to_fifteen_characters_keeping_the_counter()
    {
        var name = await Generate("%Username%02#", new NamingValues(null, null, "averyveryverylongusername", null));
        Assert.Equal(15, name.Length);
        Assert.EndsWith("01", name);
    }

    [Fact]
    public async Task Without_counter_a_taken_name_is_a_conflict() =>
        await Assert.ThrowsAsync<SdsConflictException>(() => Generate("%Username", new NamingValues(null, null, "bob", null), "BOB"));

    [Fact]
    public void Rejects_unknown_tokens() =>
        Assert.Throws<SdsValidationException>(() => NamingPolicy.Validate("%Foo%#"));
}

/// <summary>Unit tests of the cleanup of temporary exports (run at server start and stop).</summary>
public class ExportCleanerTests
{
    /// <summary>
    /// Every staged export is removed, an export that is still open is skipped without failing,
    /// and the other staging folders (uploads) are left alone.
    /// </summary>
    [Fact]
    public void Clears_staged_exports_only()
    {
        var data = Path.Combine(Path.GetTempPath(), "sds-cleaner-" + Guid.NewGuid().ToString("N"));
        try
        {
            var layout = new ShareLayout(Microsoft.Extensions.Options.Options.Create(new SdsOptions
            {
                RootOverride = Path.Combine(data, "SDSREMINST"),
                DataDirectory = data,
                PublicServerName = "sds01",
            }));

            // Two finished exports, one still open, and an upload that must survive.
            string MakeExport()
            {
                var dir = Directory.CreateDirectory(Path.Combine(layout.ExportStaging, Guid.NewGuid().ToString("N")));
                var file = Path.Combine(dir.FullName, "install.wim");
                File.WriteAllText(file, "wim");
                return file;
            }

            MakeExport();
            MakeExport();
            var open = MakeExport();
            var upload = Path.Combine(layout.Staging, "uploads", "u1", "a.wim");
            Directory.CreateDirectory(Path.GetDirectoryName(upload)!);
            File.WriteAllText(upload, "upload");

            var cleaner = new Sds.Core.Maintenance.ExportCleaner(layout, Microsoft.Extensions.Logging.Abstractions.NullLogger<Sds.Core.Maintenance.ExportCleaner>.Instance);
            using (new FileStream(open, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Equal(2, cleaner.ClearAll("test"));
            }

            Assert.True(File.Exists(open));
            Assert.True(File.Exists(upload));

            // Once the file is closed the next pass (the following startup) removes it.
            Assert.Equal(1, cleaner.ClearAll("test"));
            Assert.Empty(Directory.EnumerateFileSystemEntries(layout.ExportStaging));
            Assert.Equal(0, new Sds.Core.Maintenance.ExportCleaner(new ShareLayout(Microsoft.Extensions.Options.Options.Create(new SdsOptions
            {
                RootOverride = Path.Combine(data, "SDSREMINST"),
                DataDirectory = Path.Combine(data, "missing"),
                PublicServerName = "sds01",
            })), Microsoft.Extensions.Logging.Abstractions.NullLogger<Sds.Core.Maintenance.ExportCleaner>.Instance).ClearAll("test"));
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }
}

/// <summary>Unit tests of identifiers, driver filters and folder naming.</summary>
public class MiscTests
{
    [Theory]
    [InlineData("00:11:22:aa:bb:cc", "001122AABBCC")]
    [InlineData("00-11-22-AA-BB-CC", "001122AABBCC")]
    [InlineData("0011.22aa.bbcc", "001122AABBCC")]
    [InlineData(null, null)]
    public void Normalizes_mac(string? input, string? expected) => Assert.Equal(expected, Identifiers.NormalizeMac(input));

    [Fact]
    public void Rejects_bad_mac_and_ignores_meaningless_guids()
    {
        Assert.Throws<SdsValidationException>(() => Identifiers.NormalizeMac("00:11:22"));
        Assert.Null(Identifiers.NormalizeGuid("00000000-0000-0000-0000-000000000000"));
        Assert.Null(Identifiers.NormalizeGuid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"));
    }

    [Fact]
    public void Driver_folder_name_round_trips()
    {
        var id = Guid.NewGuid();
        var folder = ShareLayout.DriverFolderName("e1d68x64.inf", "amd64", id);
        Assert.Equal($"e1d68x64_amd64_{id.ToString("B").ToUpperInvariant()}", folder);
        Assert.True(ShareLayout.TryParseDriverFolderName(folder, out var name, out var arch, out var parsed));
        Assert.Equal(("e1d68x64", "amd64", id), (name, arch, parsed));
    }

    [Theory]
    [InlineData(DriverFilterType.Manufacturer, DriverFilterPolicy.Include, "dell inc.", true)]
    [InlineData(DriverFilterType.Manufacturer, DriverFilterPolicy.Include, "HP", false)]
    [InlineData(DriverFilterType.Manufacturer, DriverFilterPolicy.Exclude, "HP", true)]
    [InlineData(DriverFilterType.Manufacturer, DriverFilterPolicy.Include, null, false)]
    [InlineData(DriverFilterType.Manufacturer, DriverFilterPolicy.Exclude, null, true)]
    public void Driver_filter_include_exclude(DriverFilterType type, DriverFilterPolicy policy, string? value, bool expected)
    {
        var filter = new DriverGroupFilter { Type = type, Policy = policy, Values = ["Dell Inc."] };
        Assert.Equal(expected, DriverGroupService.FilterMatches(filter, value, null));
    }

    [Fact]
    public void Os_version_filter_matches_dotted_prefix()
    {
        var filter = new DriverGroupFilter { Type = DriverFilterType.OsVersion, Policy = DriverFilterPolicy.Include, Values = ["10.0.2"] };
        Assert.False(DriverGroupService.FilterMatches(filter, "10.0.22621.1", null)); // Not a dotted boundary.
        filter.Values = ["10.0"];
        Assert.True(DriverGroupService.FilterMatches(filter, "10.0.22621.1", null));
    }
}
