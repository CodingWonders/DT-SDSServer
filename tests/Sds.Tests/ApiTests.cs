using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sds.Tests;

/// <summary>
/// Hosts the real server in memory against a temporary share root and database, with a test
/// authentication scheme standing in for Windows Negotiate.
/// </summary>
public sealed class SdsFactory : WebApplicationFactory<Program>
{
    /// <summary>Temporary root of the test instance.</summary>
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "sds-api-tests", Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Sds:RootOverride", Path.Combine(Root, "SDSREMINST"));
        builder.UseSetting("Sds:DataDirectory", Path.Combine(Root, "data"));
        builder.UseSetting("Sds:CreateShares", "false");
        builder.UseSetting("Sds:CreateLocalGroups", "false");
        builder.UseSetting("Sds:CreateFirewallRule", "false");
        builder.UseSetting("Sds:ApplyDefaultAcls", "false");
        builder.UseSetting("Sds:EventLog", "false");
        builder.UseSetting("Sds:PublicServerName", "sds01");
        builder.UseSetting("Sds:TestAuthentication", "true");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(o =>
            {
                o.DefaultScheme = TestAuthHandler.SchemeName;
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}

/// <summary>
/// Test authentication: header "X-Test-User: admin" authenticates as a member of
/// BUILTIN\Administrators, "X-Test-User: user" as a plain user, no header is anonymous.
/// </summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>Scheme name.</summary>
    public const string SchemeName = "Test";

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, $"CONTOSO\\{user}") };
        if (user == "admin")
        {
            claims.Add(new Claim(ClaimTypes.GroupSid, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

/// <summary>End to end API scenarios.</summary>
public class ApiTests : IClassFixture<SdsFactory>, IClassFixture<TestWim>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly SdsFactory _factory;
    private readonly TestWim _wim;

    /// <summary>Creates the test class.</summary>
    public ApiTests(SdsFactory factory, TestWim wim)
    {
        _factory = factory;
        _wim = wim;
    }

    private HttpClient Client(string? user = "admin")
    {
        var client = _factory.CreateClient();
        if (user is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-User", user);
        }

        return client;
    }

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonNode.Parse(body)!;
    }

    private static async Task<JsonNode> WaitForJobAsync(HttpClient client, JsonNode job)
    {
        var id = job["id"]!.GetValue<string>();
        for (int i = 0; i < 600; i++)
        {
            var current = await ReadAsync(await client.GetAsync($"/api/jobs/{id}"));
            var status = current["status"]!.GetValue<string>();
            if (status == "Succeeded")
            {
                return current;
            }

            Assert.NotEqual("Failed", status + ": " + current["error"]);
            await Task.Delay(100);
        }

        throw new TimeoutException("Job did not finish.");
    }

    [Fact]
    public async Task Security_anonymous_and_non_admin_are_refused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/api/image-groups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client("user").GetAsync("/api/image-groups")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client("admin").GetAsync("/api/image-groups")).StatusCode);
    }

    [Fact]
    public async Task Auth_me_reports_account_and_role()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/api/auth/me")).StatusCode);

        var admin = await ReadAsync(await Client("admin").GetAsync("/api/auth/me"));
        Assert.Equal("CONTOSO\\admin", admin["name"]!.GetValue<string>());
        Assert.Equal("Admin", admin["role"]!.GetValue<string>());
        Assert.True(admin["isReader"]!.GetValue<bool>());

        var user = await ReadAsync(await Client("user").GetAsync("/api/auth/me"));
        Assert.Equal("None", user["role"]!.GetValue<string>());
        Assert.False(user["isAdmin"]!.GetValue<bool>());
        Assert.False(user["isReader"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Share_layout_is_created()
    {
        var info = await ReadAsync(await Client().GetAsync("/api/server"));
        var root = info["root"]!.GetValue<string>();
        Assert.True(Directory.Exists(Path.Combine(root, "PXECORE")));
        Assert.True(Directory.Exists(Path.Combine(root, "IMAGES")));
        Assert.True(Directory.Exists(Path.Combine(root, "DRIVERS")));
        Assert.Equal(["DRIVERS", "IMAGES", "PXECORE"], Directory.GetDirectories(root).Select(d => Path.GetFileName(d)!).Order().ToArray());
    }

    [Fact]
    public async Task Image_lifecycle_add_list_update_export_copy_delete()
    {
        var client = Client();
        var group = "Win" + Guid.NewGuid().ToString("N")[..6];
        var other = group + "B";
        await ReadAsync(await client.PostAsJsonAsync("/api/image-groups", new { name = group }));
        await ReadAsync(await client.PostAsJsonAsync("/api/image-groups", new { name = other }));

        // Inspect then add both images of the source (WDS split).
        var inspect = await ReadAsync(await client.PostAsJsonAsync("/api/images/inspect", new { sourcePath = _wim.WimPath }));
        Assert.Equal(2, inspect.AsArray().Count);
        var add = await client.PostAsJsonAsync($"/api/image-groups/{group}/images", new { sourcePath = _wim.WimPath });
        Assert.Equal(HttpStatusCode.Accepted, add.StatusCode);
        var job = await WaitForJobAsync(client, await ReadAsync(add));
        Assert.Equal(100, job["progress"]!.GetValue<int>());

        var images = (await ReadAsync(await client.GetAsync($"/api/image-groups/{group}/images"))).AsArray();
        Assert.Equal(["install.wim", "install-(2).wim"], images.Select(i => i!["fileName"]!.GetValue<string>()).Order().Reverse().ToArray());
        var groupDir = Path.Combine(_factory.Root, "SDSREMINST", "IMAGES", group);
        Assert.True(File.Exists(Path.Combine(groupDir, "Res.RWM")));
        Assert.StartsWith(@"\\sds01\images\", images[0]!["metadataUnc"]!.GetValue<string>());

        // Update name and priority; the name lands in the WIM XML too.
        var id = images.First(i => i!["fileName"]!.GetValue<string>() == "install-(2).wim")!["id"]!.GetValue<int>();
        var updated = await ReadAsync(await client.PatchAsJsonAsync($"/api/images/{id}", new { name = "Renamed Two", priority = 10 }));
        Assert.Equal("Renamed Two", updated["name"]!.GetValue<string>());
        var reread = Sds.Imaging.WdsImageFormat.ReadImageInfo(Path.Combine(groupDir, "install-(2).wim"), Path.Combine(groupDir, "Res.RWM"), _wim.Temp);
        Assert.Equal("Renamed Two", reread.Name);

        // Image unattend in the WDS location.
        var put = new HttpRequestMessage(HttpMethod.Put, $"/api/images/{id}/unattend") { Content = new StringContent("<unattend xmlns=\"urn:schemas-microsoft-com:unattend\" />", System.Text.Encoding.UTF8, "application/xml") };
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(put)).StatusCode);
        Assert.True(File.Exists(Path.Combine(groupDir, "install-(2)", "Unattend", "ImageUnattend.xml")));

        // Export to staging and download the standalone WIM.
        var export = await WaitForJobAsync(client, await ReadAsync(await client.PostAsJsonAsync($"/api/images/{id}/export", new { })));
        var download = await client.GetAsync($"/api/jobs/{export["id"]}/file");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.True((await download.Content.ReadAsByteArrayAsync()).Length > 3_900_000);

        // Move to the other group keeps name, priority and unattend.
        var moved = await WaitForJobAsync(client, await ReadAsync(await client.PostAsJsonAsync($"/api/images/{id}/move", new { targetGroup = other })));
        var movedImage = JsonNode.Parse(moved["resultJson"]!.GetValue<string>())!;
        Assert.Equal("Renamed Two", movedImage["name"]!.GetValue<string>());
        Assert.True(movedImage["hasUnattend"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/images/{id}")).StatusCode);

        // Client view: anonymous image listing is refused, authenticated users see enabled images.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/api/client/images")).StatusCode);

        // The client list carries the fields WDS based scripts select on (file name, group, priority).
        var clientImages = (await ReadAsync(await client.GetAsync($"/api/client/images?group={other}"))).AsArray();
        var clientMoved = Assert.Single(clientImages)!;
        Assert.Equal("install-(2).wim", clientMoved["fileName"]!.GetValue<string>());
        Assert.Equal(other, clientMoved["group"]!.GetValue<string>());
        Assert.Equal(10, clientMoved["priority"]!.GetValue<int>());

        // Deleting the group with images requires force.
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/image-groups/{group}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/image-groups/{group}?force=true")).StatusCode);
        Assert.False(Directory.Exists(groupDir));
    }

    [Fact]
    public async Task Drivers_import_into_named_folders_and_match_through_groups()
    {
        var client = Client();
        var source = Path.Combine(_wim.Root, "drv" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(source, "files", "sub"));
        File.WriteAllText(Path.Combine(source, "contoso.inf"), InfParserTests.SampleInf);
        File.WriteAllText(Path.Combine(source, "sample.cat"), "cat");
        File.WriteAllText(Path.Combine(source, "files", "sample.sys"), "sys");
        File.WriteAllText(Path.Combine(source, "files", "sub", "helper.dll"), "dll");

        var groupName = "NIC" + Guid.NewGuid().ToString("N")[..6];
        await ReadAsync(await client.PostAsJsonAsync("/api/driver-groups", new
        {
            name = groupName,
            applicability = "Matching",
            filters = new[] { new { type = "Manufacturer", policy = "Include", values = new[] { "Contoso Computers" } } },
        }));

        var job = await WaitForJobAsync(client, await ReadAsync(await client.PostAsJsonAsync("/api/drivers/import", new { sourcePath = source, driverGroups = new[] { groupName } })));
        var result = JsonNode.Parse(job["resultJson"]!.GetValue<string>())!;
        var imported = result["imported"]!.AsArray();
        Assert.Equal(3, imported.Count); // amd64, arm64, x86.

        // Folder naming <drvname>_<arch>_{GUID} and referenced files copied.
        var amd64 = imported.First(p => p!["architecture"]!.GetValue<string>() == "amd64")!;
        var folder = amd64["folderName"]!.GetValue<string>();
        Assert.Matches(@"^contoso_amd64_\{[0-9A-F-]{36}\}$", folder);
        var dir = Path.Combine(_factory.Root, "SDSREMINST", "DRIVERS", folder);
        Assert.True(File.Exists(Path.Combine(dir, "contoso.inf")));
        Assert.True(File.Exists(Path.Combine(dir, "files", "sub", "helper.dll")));
        Assert.True(amd64["bootCritical"]!.GetValue<bool>());

        // Re-import is detected as duplicate.
        var again = await WaitForJobAsync(client, await ReadAsync(await client.PostAsJsonAsync("/api/drivers/import", new { sourcePath = source })));
        Assert.Equal(3, JsonNode.Parse(again["resultJson"]!.GetValue<string>())!["duplicates"]!.AsArray().Count);

        // Matching client gets the amd64 package; a different manufacturer gets nothing.
        var query = new { architecture = "x64", manufacturer = "Contoso Computers", hardwareIds = new[] { @"PCI\VEN_8086&DEV_15BB&SUBSYS_0001" , @"PCI\VEN_8086&DEV_15BB" } };
        var matches = (await ReadAsync(await client.PostAsJsonAsync("/api/client/drivers/query", query))).AsArray();
        var match = Assert.Single(matches);
        Assert.Equal(amd64["id"]!.GetValue<string>(), match!["packageId"]!.GetValue<string>());
        var none = (await ReadAsync(await client.PostAsJsonAsync("/api/client/drivers/query", query with { manufacturer = "Fabrikam" }))).AsArray();
        Assert.Empty(none);
    }

    [Fact]
    public async Task Computer_authorization_known_pending_approve_reject()
    {
        var admin = Client();
        var anonymous = Client(null);

        // Policy: respond to all, approval required, short naming policy.
        var settings = await ReadAsync(await admin.GetAsync("/api/server/settings"));
        settings["responsePolicy"] = "All";
        settings["requireApprovalForUnknown"] = true;
        settings["namingPolicy"] = "LAB-%02#";
        await ReadAsync(await admin.PutAsync("/api/server/settings", JsonContent.Create(settings)));

        // Prestaged computer is Known immediately.
        var mac = "00:15:5D:" + Random.Shared.Next(0x10, 0xFF).ToString("X2") + ":00:01";
        var prestaged = await ReadAsync(await admin.PostAsJsonAsync("/api/computers", new { name = "PC" + Random.Shared.Next(1000, 9999), macAddress = mac, domain = "contoso.com" }));
        var known = await ReadAsync(await anonymous.PostAsJsonAsync("/api/client/register", new { macAddress = mac, architecture = "x64" }));
        Assert.Equal("Known", known["status"]!.GetValue<string>());
        Assert.Equal(prestaged["name"]!.GetValue<string>(), known["configuration"]!["name"]!.GetValue<string>());

        // Unknown computer goes pending, then is approved with a generated name.
        var guid = Guid.NewGuid().ToString();
        var pending = await ReadAsync(await anonymous.PostAsJsonAsync("/api/client/register", new { smbiosGuid = guid, architecture = "amd64" }));
        Assert.Equal("Pending", pending["status"]!.GetValue<string>());
        var requestId = pending["requestId"]!.GetValue<long>();

        // A reboot reuses the same request.
        var again = await ReadAsync(await anonymous.PostAsJsonAsync("/api/client/register", new { smbiosGuid = guid }));
        Assert.Equal(requestId, again["requestId"]!.GetValue<long>());

        var approved = await ReadAsync(await admin.PostAsJsonAsync($"/api/pending-devices/{requestId}/approve", new { }));
        Assert.Equal("Approved", approved["status"]!.GetValue<string>());
        var poll = await ReadAsync(await anonymous.GetAsync($"/api/client/requests/{requestId}"));
        Assert.Equal("Known", poll["status"]!.GetValue<string>());
        Assert.Matches("^LAB-\\d{2}$", poll["configuration"]!["name"]!.GetValue<string>());

        // Rejected device.
        var other = await ReadAsync(await anonymous.PostAsJsonAsync("/api/client/register", new { macAddress = "02:00:00:00:00:" + Random.Shared.Next(0x10, 0xFF).ToString("X2") }));
        var otherId = other["requestId"]!.GetValue<long>();
        await ReadAsync(await admin.PostAsJsonAsync($"/api/pending-devices/{otherId}/reject", new { message = "Not allowed" }));
        var rejected = await ReadAsync(await anonymous.GetAsync($"/api/client/requests/{otherId}"));
        Assert.Equal("Rejected", rejected["status"]!.GetValue<string>());
        Assert.Equal("Not allowed", rejected["message"]!.GetValue<string>());

        // Known only: unknown clients are ignored; deleting the computer de-authorizes it.
        settings["responsePolicy"] = "KnownOnly";
        await ReadAsync(await admin.PutAsync("/api/server/settings", JsonContent.Create(settings)));
        var ignored = await ReadAsync(await anonymous.PostAsJsonAsync("/api/client/register", new { macAddress = "02:11:22:33:44:55" }));
        Assert.Equal("Ignored", ignored["status"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/computers/{prestaged["id"]}")).StatusCode);
        var gone = await ReadAsync(await anonymous.PostAsJsonAsync("/api/client/register", new { macAddress = mac }));
        Assert.Equal("Ignored", gone["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Multicast_scheduled_cast_starts_at_threshold()
    {
        var client = Client();
        var group = "Mc" + Guid.NewGuid().ToString("N")[..6];
        await ReadAsync(await client.PostAsJsonAsync("/api/image-groups", new { name = group }));
        await WaitForJobAsync(client, await ReadAsync(await client.PostAsJsonAsync($"/api/image-groups/{group}/images", new { sourcePath = _wim.WimPath, indexes = new[] { 1 } })));
        var imageId = (await ReadAsync(await client.GetAsync($"/api/image-groups/{group}/images"))).AsArray()[0]!["id"]!.GetValue<int>();

        var t = await ReadAsync(await client.PostAsJsonAsync("/api/multicast", new { name = group, installImageId = imageId, type = "ScheduledCast", startClientThreshold = 2 }));
        Assert.Equal("Waiting", t["state"]!.GetValue<string>());
        await ReadAsync(await client.PostAsJsonAsync($"/api/client/multicast/{imageId}/join", new { macAddress = "02:00:00:00:01:01" }));
        var started = await ReadAsync(await client.PostAsJsonAsync($"/api/client/multicast/{imageId}/join", new { macAddress = "02:00:00:00:01:02" }));
        Assert.Equal("Active", started["state"]!.GetValue<string>());
        Assert.Equal(2, started["connectedClients"]!.GetValue<int>());
    }
}
