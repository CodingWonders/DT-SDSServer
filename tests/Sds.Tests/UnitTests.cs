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

/// <summary>Port detection for the startup firewall rule.</summary>
public class FirewallPortTests
{
    private static SortedSet<int> Ports(params (string Key, string Value)[] settings) =>
        Sds.Core.Networking.FirewallConfigurator.GetListenPorts(Microsoft.Extensions.Configuration.MemoryConfigurationBuilderExtensions.AddInMemoryCollection(new Microsoft.Extensions.Configuration.ConfigurationBuilder(),
                settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build());

    [Fact]
    public void Reads_wildcard_and_specific_addresses() =>
        Assert.Equal([8350, 8443], Ports(("urls", "http://*:8350;https://+:8443;http://10.0.0.5:8350")));

    [Fact]
    public void Ignores_loopback_and_dynamic_ports() =>
        Assert.Empty(Ports(("urls", "http://localhost:8350;http://127.0.0.1:8351;http://[::1]:8352;http://*:0")));

    [Fact]
    public void Reads_kestrel_endpoints_and_port_lists() =>
        Assert.Equal([80, 8350, 9000], Ports(
            ("Kestrel:Endpoints:Api:Url", "http://0.0.0.0:8350"),
            ("http_ports", "80;9000")));

    [Fact]
    public void No_configuration_means_no_rule() => Assert.Empty(Ports());
}

/// <summary>CORS for the Web Console served from the API's own machine.</summary>
public class ConsoleCorsTests
{
    private static bool Allowed(string origin, string apiHost, int consolePort = 8351) =>
        Sds.Server.Infrastructure.SdsCorsPolicyProvider.IsConsoleOnApiHost(origin, new Microsoft.AspNetCore.Http.HostString(apiHost), consolePort);

    [Theory]
    [InlineData("http://192.168.1.229:8351", "192.168.1.229:8350")]
    [InlineData("http://localhost:8351", "localhost:8350")]
    [InlineData("http://SDS01:8351", "sds01:8350")]
    [InlineData("https://sds01.contoso.com:8351", "sds01.contoso.com:8352")]
    [InlineData("http://[fe80::1]:8351", "[fe80::1]:8350")]
    public void Allows_console_on_the_host_the_browser_used(string origin, string apiHost) =>
        Assert.True(Allowed(origin, apiHost));

    [Theory]
    [InlineData("http://evil.example:8351", "192.168.1.229:8350")]
    [InlineData("http://192.168.1.229:8080", "192.168.1.229:8350")]
    [InlineData("http://192.168.1.229", "192.168.1.229:8350")]
    [InlineData("file://192.168.1.229:8351", "192.168.1.229:8350")]
    [InlineData("null", "192.168.1.229:8350")]
    [InlineData("", "192.168.1.229:8350")]
    public void Refuses_other_origins(string origin, string apiHost) =>
        Assert.False(Allowed(origin, apiHost));

    [Fact]
    public void Port_zero_turns_it_off() =>
        Assert.False(Allowed("http://192.168.1.229:8351", "192.168.1.229:8350", consolePort: 0));
}

/// <summary>The provider wired into the real CORS service, as the middleware uses it.</summary>
public class ConsoleCorsPreflightTests
{
    [Theory]
    [InlineData("http://192.168.1.229:8351", "192.168.1.229:8350", true)]
    [InlineData("http://localhost:8351", "localhost:8350", true)]
    [InlineData("http://evil.example:8351", "192.168.1.229:8350", false)]
    public async Task Preflight_from_console_origin(string origin, string apiHost, bool allowed)
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var provider = new Sds.Server.Infrastructure.SdsCorsPolicyProvider(configuration);

        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Method = "OPTIONS";
        context.Request.Host = new Microsoft.AspNetCore.Http.HostString(apiHost);
        context.Request.Headers.Origin = origin;
        context.Request.Headers.AccessControlRequestMethod = "POST";
        context.Request.Headers.AccessControlRequestHeaders = "content-type,x-sds-request";

        var policy = await provider.GetPolicyAsync(context, null);
        var service = new Microsoft.AspNetCore.Cors.Infrastructure.CorsService(
            Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        var result = service.EvaluatePolicy(context, policy!);

        Assert.Equal(allowed, result.IsOriginAllowed);
    }
}

/// <summary>The HTTPS endpoint the server adds by itself when its certificate is installed.</summary>
public class AutomaticHttpsTests
{
    private static System.Security.Cryptography.X509Certificates.X509Certificate2 NewCertificate()
    {
        using var key = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=SDS Server HTTPS", key, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
    }

    private static (string Status, Microsoft.Extensions.Configuration.ConfigurationManager Config, System.Security.Authentication.SslProtocols? Protocols, string? Subject) Run(
        bool certificateInstalled, params (string Key, string Value)[] settings)
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        Microsoft.Extensions.Configuration.MemoryConfigurationBuilderExtensions.AddInMemoryCollection(config,
            settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)));

        string? askedSubject = null;
        System.Security.Authentication.SslProtocols? protocols = null;
        var status = Sds.Server.Infrastructure.AutomaticHttps.Configure(
            config,
            subject =>
            {
                askedSubject = subject;
                return certificateInstalled ? (NewCertificate(), System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser) : null;
            },
            (_, p) => protocols = p);
        return (status, config, protocols, askedSubject);
    }

    [Fact]
    public void Adds_https_next_to_every_http_address()
    {
        var (status, config, protocols, subject) = Run(true, ("urls", "http://*:8350;http://localhost:9000"));

        Assert.StartsWith("HTTPS is on: port 8352", status);
        Assert.Equal("SDS Server HTTPS", subject);
        Assert.Equal("http://*:8350", config["Kestrel:Endpoints:Http0:Url"]);
        Assert.Equal("http://localhost:9000", config["Kestrel:Endpoints:Http1:Url"]);
        Assert.Equal("https://*:8352", config["Kestrel:Endpoints:AutoHttps:Url"]);
        Assert.Equal(System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13, protocols);
        // The firewall rule follows the same settings.
        Assert.Equal([8350, 8352], Sds.Core.Networking.FirewallConfigurator.GetListenPorts(config));
    }

    [Fact]
    public void Uses_configured_port_and_subject() =>
        Assert.Equal(("https://*:9443", "Lab cert"), Run(true, ("urls", "http://*:8350"), ("Sds:HttpsPort", "9443"), ("Sds:HttpsCertificateSubject", "Lab cert"))
            is var r ? (r.Config["Kestrel:Endpoints:AutoHttps:Url"], r.Subject) : default);

    [Fact]
    public void Without_certificate_nothing_changes()
    {
        var (status, config, protocols, _) = Run(false, ("urls", "http://*:8350"));
        Assert.StartsWith("HTTPS is off", status);
        Assert.Empty(config.GetSection("Kestrel:Endpoints").GetChildren());
        Assert.Null(protocols);
    }

    [Fact]
    public void Own_kestrel_endpoints_win()
    {
        var (status, config, protocols, subject) = Run(true, ("Kestrel:Endpoints:Mine:Url", "https://*:443"));
        Assert.Contains("Kestrel:Endpoints is configured", status);
        Assert.Single(config.GetSection("Kestrel:Endpoints").GetChildren());
        Assert.Null(protocols);
        Assert.Null(subject);
    }

    [Fact]
    public void Port_zero_turns_it_off()
    {
        var (status, _, protocols, subject) = Run(true, ("Sds:HttpsPort", "0"));
        Assert.Contains("turned off", status);
        Assert.Null(protocols);
        Assert.Null(subject);
    }
}

/// <summary>GET /api/trust: back to the console after the certificate is accepted, never elsewhere.</summary>
public class TrustRedirectTests
{
    private static Microsoft.AspNetCore.Mvc.IActionResult Get(string? returnUrl, string apiHost = "192.168.1.229:8352", params (string Key, string Value)[] settings)
    {
        var configuration = Microsoft.Extensions.Configuration.MemoryConfigurationBuilderExtensions.AddInMemoryCollection(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder(),
                settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var controller = new Sds.Server.Controllers.TrustController(new Sds.Server.Infrastructure.SdsCorsPolicyProvider(configuration))
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() },
            ProblemDetailsFactory = new TestProblemDetailsFactory(),
        };
        controller.HttpContext.Request.Host = new Microsoft.AspNetCore.Http.HostString(apiHost);
        return controller.Get(returnUrl);
    }

    [Theory]
    [InlineData("http://192.168.1.229:8351/")]
    [InlineData("http://192.168.1.229:8351/#images")]
    [InlineData("http://192.168.1.229:8351/index.html?x=1")]
    public void Redirects_back_to_the_console_on_the_api_host(string returnUrl) =>
        Assert.Equal(new Uri(returnUrl).AbsoluteUri, Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectResult>(Get(returnUrl)).Url);

    [Fact]
    public void Redirects_to_a_listed_origin() =>
        Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectResult>(
            Get("https://sdsconsole.contoso.com/", "sds01:8352", ("Sds:CorsOrigins:0", "https://sdsconsole.contoso.com")));

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("http://192.168.1.229:9999/")]
    [InlineData("http://user@192.168.1.229:8351/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.example/")]
    [InlineData("/relative")]
    public void Refuses_other_addresses(string returnUrl) =>
        Assert.Equal(400, Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(Get(returnUrl)).StatusCode);

    [Fact]
    public void Without_return_answers_no_content() =>
        Assert.IsType<Microsoft.AspNetCore.Mvc.NoContentResult>(Get(null));

    private sealed class TestProblemDetailsFactory : Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory
    {
        public override Microsoft.AspNetCore.Mvc.ProblemDetails CreateProblemDetails(Microsoft.AspNetCore.Http.HttpContext httpContext, int? statusCode = null, string? title = null, string? type = null, string? detail = null, string? instance = null) =>
            new() { Status = statusCode, Title = title, Detail = detail };

        public override Microsoft.AspNetCore.Mvc.ValidationProblemDetails CreateValidationProblemDetails(Microsoft.AspNetCore.Http.HttpContext httpContext, Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelStateDictionary, int? statusCode = null, string? title = null, string? type = null, string? detail = null, string? instance = null) =>
            new(modelStateDictionary) { Status = statusCode };
    }
}
