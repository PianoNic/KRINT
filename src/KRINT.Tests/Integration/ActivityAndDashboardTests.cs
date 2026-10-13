using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TUnit.Core;

namespace KRINT.Tests.Integration;

/// <summary>The activity log and the dashboard stats both reflect a new instance.</summary>
[NotInParallel("integration")]
public class ActivityAndDashboardTests
{
    [Test]
    public async Task Activity_RecordsInstanceCreate()
    {
        var api = await KrintApi.GetAsync();
        await using var instance = await api.ProvisionAsync("postgres", "18", "act");

        // Activity rows reference instances by their docker container name.
        var entries = (await api.GetAsync("/api/Activity")).AsArray();
        await Assert.That(entries.Any(e => IsCreateOf(e, instance))).IsTrue();
    }

    [Test]
    public async Task Dashboard_CountsNewInstanceAndShowsItsActivity()
    {
        var api = await KrintApi.GetAsync();
        var before = (await api.GetAsync("/api/Dashboard/stats"))["totalInstances"]!.GetValue<int>();

        await using var instance = await api.ProvisionAsync("postgres", "18", "dash");

        var stats = await api.GetAsync("/api/Dashboard/stats");
        await Assert.That(stats["totalInstances"]!.GetValue<int>()).IsEqualTo(before + 1);
        await Assert.That(stats["runningInstances"]!.GetValue<int>()).IsGreaterThanOrEqualTo(1);
        var recent = stats["recentActivity"]!.AsArray();
        await Assert.That(recent.Any(e => e!["target"]!.GetValue<string>() == instance.ContainerName)).IsTrue();
    }

    private static bool IsCreateOf(JsonNode? entry, KrintApi.Instance instance) =>
        entry!["action"]!.GetValue<string>() == "instance.create"
        && entry["target"]!.GetValue<string>() == instance.ContainerName
        && entry["instanceId"]?.GetValue<Guid>() == instance.Id;
}
