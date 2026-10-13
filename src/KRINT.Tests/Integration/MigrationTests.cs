using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using TUnit.Core;

namespace KRINT.Tests.Integration;

/// <summary>Guided migration of a compose-managed container into KRINT.</summary>
[NotInParallel("integration")]
public class MigrationTests
{
    /// <summary>
    /// A compose-labelled source container is discovered with its compose metadata, and the
    /// migration stream moves its data into a new managed instance.
    /// </summary>
    [Test]
    public async Task Migration_DiscoversComposeSourceAndCopiesData()
    {
        var api = await KrintApi.GetAsync();
        await using var source = await MigrationSourceContainer.StartPostgresAsync();
        await DockerCli.ExecAsync(source.ContainerName, "psql", "-U", "postgres", "-d", source.Database, "-c",
            "CREATE TABLE migrated (id int); INSERT INTO migrated SELECT generate_series(1, 7);");

        var candidates = (await api.GetAsync("/api/Database/discover")).AsArray();
        var candidate = candidates.FirstOrDefault(c => c!["containerName"]!.GetValue<string>() == source.ContainerName);
        await Assert.That(candidate).IsNotNull();
        await Assert.That(candidate!["engine"]!.GetValue<string>()).IsEqualTo("postgres");
        await Assert.That(candidate["state"]!.GetValue<string>()).IsEqualTo("running");
        await Assert.That(candidate["composeProject"]!.GetValue<string>()).IsEqualTo(source.ComposeProject);
        await Assert.That(candidate["composeService"]!.GetValue<string>()).IsEqualTo(source.ComposeService);
        await Assert.That(candidate["databaseName"]!.GetValue<string>()).IsEqualTo(source.Database);
        await Assert.That(candidate["passwordAvailable"]!.GetValue<bool>()).IsTrue();

        var targetName = $"it_mig_{DateTime.UtcNow:HHmmssfff}";
        await using var hub = await HubClient.ConnectAsync(api, "migration");
        var request = new
        {
            sourceContainerId = candidate["containerId"]!.GetValue<string>(),
            sourceHost = candidate["host"]!.GetValue<string>(),
            sourcePort = candidate["port"]!.GetValue<int>(),
            sourceUsername = candidate["username"]!.GetValue<string>(),
            sourcePassword = source.Password,
            sourceDatabaseName = source.Database,
            sourceEngine = "postgres",
            targetEngine = "postgres",
            targetVersion = "18",
            targetDisplayName = targetName,
            composeProject = source.ComposeProject,
            composeService = source.ComposeService,
            composeFilePath = source.ComposeFile,
        };

        JsonElement? last = null;
        var steps = new List<string>();
        await foreach (var ev in hub.StreamAsync<JsonElement>("StreamMigration", request))
        {
            last = ev;
            steps.Add($"{ev.GetProperty("step").GetString()}:{ev.GetProperty("status").GetString()} {ev.GetProperty("message").GetString()}");
        }

        // Clean up whatever instance the migration created, whatever the outcome.
        var created = (await api.GetAsync($"/api/Database?displayName={targetName}")).AsArray().FirstOrDefault();
        try
        {
            await Assert.That(last?.GetProperty("status").GetString()).IsEqualTo("done")
                .Because(string.Join(" | ", steps) + " | error: " + (last?.TryGetProperty("error", out var e) == true ? e.ToString() : ""));
            await Assert.That(created).IsNotNull();
            var id = created!["id"]!.GetValue<Guid>();
            var dbName = created["databaseName"]!.GetValue<string>();
            var count = await KrintApi.EventuallyAsync(() => api.ScalarAsync(id, dbName, "SELECT count(*) FROM migrated"));
            await Assert.That(count).IsEqualTo("7");
        }
        finally
        {
            if (created is not null)
                await api.SendAsync(System.Net.Http.HttpMethod.Delete, $"/api/Database/{created["id"]!.GetValue<Guid>()}");
        }
    }
}
