using System.Threading.Tasks;
using TUnit.Core;

namespace KRINT.Tests.Integration;

/// <summary>Logical databases and login users inside an instance.</summary>
[NotInParallel("integration")]
public class InnerDatabaseAndUserTests
{
    [Test]
    public async Task InnerDatabase_CreateListAndDrop()
    {
        var api = await KrintApi.GetAsync();
        await using var instance = await api.ProvisionAsync("postgres", "18", "idb");

        await api.PostAsync($"/api/Database/{instance.Id}/databases", new { name = "orders" });
        await api.PostAsync($"/api/Database/{instance.Id}/databases", new { name = "todrop" });
        var listed = await InnerDatabasesAsync(api, instance.Id);
        await Assert.That(listed).Contains("orders");
        await Assert.That(listed).Contains("todrop");

        await api.DeleteAsync($"/api/Database/{instance.Id}/databases/todrop");
        listed = await InnerDatabasesAsync(api, instance.Id);
        await Assert.That(listed).Contains("orders");
        await Assert.That(listed).DoesNotContain("todrop");
    }

    [Test]
    public async Task User_CreateGrantResetAndDelete()
    {
        var api = await KrintApi.GetAsync();
        await using var instance = await api.ProvisionAsync("postgres", "18", "usr");

        var created = await api.PostAsync($"/api/Database/{instance.Id}/users", new { name = "svcuser" });
        var firstPassword = created!["password"]!.GetValue<string>();
        await Assert.That(created["name"]!.GetValue<string>()).IsEqualTo("svcuser");
        await Assert.That(firstPassword).IsNotEmpty();
        await Assert.That(await UsersAsync(api, instance.Id)).Contains("svcuser");

        await api.PostAsync($"/api/Database/{instance.Id}/users/svcuser/grants", new { database = "postgres" });
        var who = await Postgres.PsqlAsync(instance.ContainerName, "svcuser", firstPassword, "postgres", "SELECT current_user");
        await Assert.That(who).IsEqualTo("svcuser");

        var reset = await api.PostAsync($"/api/Database/{instance.Id}/users/svcuser/reset-password");
        var secondPassword = reset!["password"]!.GetValue<string>();
        await Assert.That(secondPassword).IsNotEqualTo(firstPassword);
        // The new password works and the old one no longer does.
        await Assert.That(await Postgres.PsqlAsync(instance.ContainerName, "svcuser", secondPassword, "postgres", "SELECT 1")).IsEqualTo("1");
        await Assert.That(await Postgres.CanLoginAsync(instance.ContainerName, "svcuser", firstPassword, "postgres")).IsFalse();

        await api.DeleteAsync($"/api/Database/{instance.Id}/users/svcuser");
        await Assert.That(await UsersAsync(api, instance.Id)).DoesNotContain("svcuser");
    }

    private static async Task<List<string>> InnerDatabasesAsync(KrintApi api, Guid id) =>
        (await api.GetAsync($"/api/Database/{id}/databases")).AsArray().Select(n => n!.GetValue<string>()).ToList();

    private static async Task<List<string>> UsersAsync(KrintApi api, Guid id) =>
        (await api.GetAsync($"/api/Database/{id}/users")).AsArray().Select(n => n!.GetValue<string>()).ToList();
}
