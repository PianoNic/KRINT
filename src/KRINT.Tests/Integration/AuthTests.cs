using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TUnit.Core;

namespace KRINT.Tests.Integration;

/// <summary>Local login, the guard on the API, and the read-only pages' backing endpoints.</summary>
[NotInParallel("integration")]
public class AuthTests
{
    [Test]
    public async Task App_ReportsLocalLoginMode()
    {
        var api = await KrintApi.GetAsync();
        using var res = await api.SendAsync(HttpMethod.Get, "/api/App", authenticated: false);
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
        await Assert.That(body.ToJsonString()).Contains("\"local\"");
    }

    [Test]
    public async Task Login_RejectsWrongPassword()
    {
        var api = await KrintApi.GetAsync();
        using var res = await api.LoginAsync(KrintStack.AdminUserName, "definitely-not-the-password");
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Api_RejectsAnonymousCalls()
    {
        var api = await KrintApi.GetAsync();
        using var res = await api.SendAsync(HttpMethod.Get, "/api/Database", authenticated: false);
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Settings_ListsPortRangesAndEngines()
    {
        var api = await KrintApi.GetAsync();
        var settings = await api.GetAsync("/api/Settings");
        await Assert.That(settings["portRanges"]!.AsArray().Count).IsGreaterThan(0);
        await Assert.That(settings["supportedEngines"]!.AsArray().Count).IsGreaterThan(0);
        await Assert.That(settings["vaultMasterKeyConfigured"]!.GetValue<bool>()).IsTrue();
    }
}
