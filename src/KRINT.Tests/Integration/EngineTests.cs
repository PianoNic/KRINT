using System.Threading.Tasks;
using TUnit.Core;

namespace KRINT.Tests.Integration;

/// <summary>
/// One provision per engine family beyond Postgres, on the newest catalog version: the instance
/// comes up running, its connection string carries the engine's scheme, and it lists. Engines are
/// picked for image size and boot time; SQL Server is covered by <see cref="DataIntegrityTests"/>.
/// </summary>
[NotInParallel("integration")]
public class EngineTests
{
    [Test]
    [Arguments("mysql", "mysql://")]
    [Arguments("mariadb", "mariadb://")]
    [Arguments("mongo", "mongodb://")]
    [Arguments("redis", "redis://")]
    [Arguments("valkey", "redis://")]
    [Arguments("clickhouse", "http://")]
    public async Task Engine_ProvisionsAndLists(string engine, string scheme)
    {
        var api = await KrintApi.GetAsync();
        var version = await api.LatestVersionAsync(engine);
        await using var instance = await api.ProvisionAsync(engine, version, engine);

        await Assert.That(instance.ConnectionString).StartsWith(scheme);
        var row = await api.FindInstanceAsync(instance.Id);
        await Assert.That(row).IsNotNull();
        await Assert.That(row!["engine"]!.GetValue<string>()).IsEqualTo(engine);
        await Assert.That(row["state"]!.GetValue<string>()).IsEqualTo("running");
    }
}
