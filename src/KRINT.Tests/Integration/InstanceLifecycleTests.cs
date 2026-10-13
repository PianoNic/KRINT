using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using TUnit.Core;

namespace KRINT.Tests.Integration;

/// <summary>Provision, list, details, rename, stop/start and delete of a managed instance.</summary>
[NotInParallel("integration")]
public class InstanceLifecycleTests
{
    [Test]
    public async Task Provision_ListsInstanceWithDetailsAndConnectionString()
    {
        var api = await KrintApi.GetAsync();
        var dbName = "named_" + DateTime.UtcNow.ToString("HHmmssfff");
        await using var instance = await api.ProvisionAsync("postgres", "18", "life", defaultDatabase: dbName);

        await Assert.That(instance.ConnectionString).StartsWith("postgres://");
        await Assert.That(instance.Password).IsNotEmpty();

        var row = await api.FindInstanceAsync(instance.Id);
        await Assert.That(row).IsNotNull();
        await Assert.That(row!["displayName"]!.GetValue<string>()).IsEqualTo(instance.DisplayName);
        await Assert.That(row["databaseName"]!.GetValue<string>()).IsEqualTo(dbName);
        await Assert.That(row["state"]!.GetValue<string>()).IsEqualTo("running");

        // The displayName filter narrows the list to an exact match.
        var filtered = (await api.GetAsync($"/api/Database?displayName={Uri.EscapeDataString(instance.DisplayName)}")).AsArray();
        await Assert.That(filtered.Count).IsEqualTo(1);

        var details = await api.GetAsync($"/api/Database/{instance.Id}");
        await Assert.That(details["connectionString"]!.GetValue<string>()).StartsWith("postgres://");
        await Assert.That(details["password"]!.GetValue<string>()).IsEqualTo(instance.Password);
        await Assert.That(details["containerName"]!.GetValue<string>()).IsEqualTo(instance.ContainerName);
    }

    [Test]
    public async Task Rename_PersistsNewDisplayName()
    {
        var api = await KrintApi.GetAsync();
        await using var instance = await api.ProvisionAsync("postgres", "18", "ren");

        var newName = "it_renamed_" + DateTime.UtcNow.ToString("HHmmssfff");
        await api.PatchAsync($"/api/Database/{instance.Id}", new { displayName = newName });

        var row = await api.FindInstanceAsync(instance.Id);
        await Assert.That(row!["displayName"]!.GetValue<string>()).IsEqualTo(newName);
    }

    [Test]
    public async Task StopAndStart_FlipsContainerState()
    {
        var api = await KrintApi.GetAsync();
        await using var instance = await api.ProvisionAsync("postgres", "18", "stst");

        await api.PostAsync($"/api/Database/{instance.Id}/stop");
        var stopped = await api.FindInstanceAsync(instance.Id);
        await Assert.That(stopped!["state"]!.GetValue<string>()).IsNotEqualTo("running");

        await api.PostAsync($"/api/Database/{instance.Id}/start");
        var started = await api.FindInstanceAsync(instance.Id);
        await Assert.That(started!["state"]!.GetValue<string>()).IsEqualTo("running");

        // The engine answers again after the restart.
        var one = await KrintApi.EventuallyAsync(() => api.ScalarAsync(instance.Id, "postgres", "SELECT 1"));
        await Assert.That(one).IsEqualTo("1");
    }

    [Test]
    public async Task Delete_RemovesRowAndDockerContainer()
    {
        var api = await KrintApi.GetAsync();
        await using var instance = await api.ProvisionAsync("postgres", "18", "del");

        await api.DeleteAsync($"/api/Database/{instance.Id}");
        instance.Deleted = true;

        await Assert.That(await api.FindInstanceAsync(instance.Id)).IsNull();
        using var details = await api.SendAsync(HttpMethod.Get, $"/api/Database/{instance.Id}");
        await Assert.That(details.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await DockerCli.ContainerExistsAsync(instance.ContainerName)).IsFalse();
    }
}
