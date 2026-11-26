using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.Json.Nodes;
using Sds.Server.Security;

namespace Sds.Tests;

/// <summary>
/// Stands in for LogonUser: "alice" / "pw" is a member of BUILTIN\Administrators, "bob" / "pw"
/// is a plain account; anything else is a wrong user name or password.
/// </summary>
public sealed class TestCredentialValidator : ICredentialValidator
{
    /// <inheritdoc />
    public ClaimsIdentity? Validate(string userName, string password)
    {
        if (password != "pw" || userName is not ("alice" or "bob"))
        {
            return null;
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, $"CONTOSO\\{userName}") };
        if (userName == "alice")
        {
            claims.Add(new Claim(ClaimTypes.GroupSid, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value));
        }

        return new ClaimsIdentity(claims, SdsAuth.PasswordScheme, ClaimTypes.Name, ClaimTypes.Role);
    }
}

/// <summary>Sign-in scenarios: Windows identity report and password sessions.</summary>
public class AuthTests(SdsFactory factory) : IClassFixture<SdsFactory>
{
    /// <summary>Client with a cookie container (the factory default), optionally a test Windows user.</summary>
    private HttpClient Client(string? windowsUser = null)
    {
        var client = factory.CreateClient();
        if (windowsUser is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-User", windowsUser);
        }

        return client;
    }

    private static HttpRequestMessage Login(string user, string password, bool header = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { userName = user, password }) };
        if (header)
        {
            request.Headers.Add(SdsAuth.RequestHeader, "1");
        }

        return request;
    }

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonNode.Parse(body)!;
    }

    [Fact]
    public async Task Auth_me_reports_account_and_role()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync("/api/auth/me")).StatusCode);

        var admin = await ReadAsync(await Client("admin").GetAsync("/api/auth/me"));
        Assert.Equal("CONTOSO\\admin", admin["name"]!.GetValue<string>());
        Assert.Equal("Admin", admin["role"]!.GetValue<string>());
        Assert.Equal("Windows", admin["method"]!.GetValue<string>());
        Assert.True(admin["isReader"]!.GetValue<bool>());

        var user = await ReadAsync(await Client("user").GetAsync("/api/auth/me"));
        Assert.Equal("None", user["role"]!.GetValue<string>());
        Assert.False(user["isAdmin"]!.GetValue<bool>());
        Assert.False(user["isReader"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Password_session_signs_in_works_and_signs_out()
    {
        var client = Client();
        Assert.False((await ReadAsync(await client.GetAsync("/api/auth/session")))["signedIn"]!.GetValue<bool>());

        var me = await ReadAsync(await client.SendAsync(Login("alice", "pw")));
        Assert.Equal("CONTOSO\\alice", me["name"]!.GetValue<string>());
        Assert.Equal("Admin", me["role"]!.GetValue<string>());
        Assert.Equal("Password", me["method"]!.GetValue<string>());

        // The session cookie now authenticates reads, and the session can be resumed.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/image-groups")).StatusCode);
        Assert.True((await ReadAsync(await client.GetAsync("/api/auth/session")))["signedIn"]!.GetValue<bool>());
        Assert.Equal("Password", (await ReadAsync(await client.GetAsync("/api/auth/me")))["method"]!.GetValue<string>());

        // Writes riding on the cookie need the CSRF header.
        var group = "Pw" + Guid.NewGuid().ToString("N")[..6];
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/image-groups", new { name = group })).StatusCode);
        var create = new HttpRequestMessage(HttpMethod.Post, "/api/image-groups") { Content = JsonContent.Create(new { name = group }) };
        create.Headers.Add(SdsAuth.RequestHeader, "1");
        Assert.True((await client.SendAsync(create)).IsSuccessStatusCode);

        // The WDS user filter needs a Windows token, so password sessions are refused there.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/client/images")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/image-groups")).StatusCode);
    }

    [Fact]
    public async Task Password_sign_in_refuses_bad_credentials_unauthorized_accounts_and_missing_header()
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Login("alice", "wrong"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Login("nobody", "pw"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Login("alice", "pw", header: false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Login("", ""))).StatusCode);

        // Valid account without an SDS role: refused, and no session is created.
        var bob = await client.SendAsync(Login("bob", "pw"));
        Assert.Equal(HttpStatusCode.Forbidden, bob.StatusCode);
        Assert.False(bob.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/image-groups")).StatusCode);
    }
}
