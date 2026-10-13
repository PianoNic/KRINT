using System.Threading.Tasks;
using TUnit.Core;

namespace KRINT.Tests.Integration;

/// <summary>
/// Operations that rebuild or rewrite an instance must keep its data, extra databases and users.
/// These flows were verified by hand once and must not regress.
/// </summary>
[NotInParallel("integration")]
public class DataIntegrityTests
{
    [Test]
    public async Task Postgres_BackupRestoreBringsBackDeletedRows()
    {
        var api = await KrintApi.GetAsync();
        await using var instance = await ShopData.ProvisionPostgresAsync(api, "17", "pgbk");

        var backupId = await api.CreateBackupAsync(instance.Id);
        await api.QueryAsync(instance.Id, ShopData.Database, "DELETE FROM items");
        await Assert.That(await ShopData.CountAsync(api, instance.Id)).IsEqualTo(0);

        await api.RestoreBackupAsync(backupId);

        await ShopData.AssertIntactAsync(api, instance);
    }

    [Test]
    public async Task Postgres_VisibilityChangeKeepsData()
    {
        var api = await KrintApi.GetAsync();
        await using var instance = await ShopData.ProvisionPostgresAsync(api, "17", "pgvis");

        var updated = await api.PostAsync($"/api/Database/{instance.Id}/visibility", new { isPublic = true });
        await Assert.That(updated!["isPublic"]!.GetValue<bool>()).IsTrue();
        await Assert.That((await api.FindInstanceAsync(instance.Id))!["isPublic"]!.GetValue<bool>()).IsTrue();

        await ShopData.AssertIntactAsync(api, instance);
        await Assert.That(await DockerCli.ContainersForInstanceAsync(instance.Id)).HasCount().EqualTo(1);
    }

    [Test]
    public async Task Postgres_Upgrade17To18KeepsDataAndLeavesOneContainer()
    {
        var api = await KrintApi.GetAsync();
        await using var instance = await ShopData.ProvisionPostgresAsync(api, "17", "pgup");

        var upgraded = await api.PostAsync($"/api/Database/{instance.Id}/upgrade", new { targetVersion = "18" });
        await Assert.That(upgraded!["version"]!.GetValue<string>()).IsEqualTo("18");
        await Assert.That(upgraded["previousVersion"]?.GetValue<string>()).IsEqualTo("17");
        await Assert.That((await api.FindInstanceAsync(instance.Id))!["version"]!.GetValue<string>()).IsEqualTo("18");

        var serverVersion = await KrintApi.EventuallyAsync(() => api.ScalarAsync(instance.Id, ShopData.Database, "SHOW server_version"));
        await Assert.That(serverVersion).StartsWith("18");

        await ShopData.AssertIntactAsync(api, instance);
        await Assert.That(await DockerCli.ContainersForInstanceAsync(instance.Id)).HasCount().EqualTo(1);
    }

    /// <summary>Keys survive FLUSHALL + restore, and then a plain container restart (AOF).</summary>
    [Test]
    public async Task Redis_BackupRestoreAndRestartKeepKeys()
    {
        var api = await KrintApi.GetAsync();
        var version = await api.LatestVersionAsync("redis");
        await using var instance = await api.ProvisionAsync("redis", version, "redis");

        await KrintApi.EventuallyAsync(async () => await RedisCli(instance, "PING") == "PONG" ? "PONG" : throw new InvalidOperationException("redis not ready"));
        await RedisCli(instance, "SET", "it:alpha", "one");
        await RedisCli(instance, "SET", "it:beta", "two");

        var backupId = await api.CreateBackupAsync(instance.Id);
        await RedisCli(instance, "FLUSHALL");
        await Assert.That(await RedisCli(instance, "DBSIZE")).IsEqualTo("0");

        await api.RestoreBackupAsync(backupId);
        await AssertKeysAsync(api, instance);

        instance.ContainerName = await CurrentContainerAsync(api, instance.Id);
        await DockerCli.RunCheckedAsync("restart", instance.ContainerName);
        await AssertKeysAsync(api, instance);
    }

    /// <summary>
    /// A table in an extra database comes back after backup, drop and restore.
    /// </summary>
    [Test]
    [Skip("MSSQL backup fails: MsSqlBackupService.DumpScript runs `sqlcmd -b -h -1 -y 0` to script logins, " +
          "and sqlcmd rejects it with 'The -h and the -y 0 options are mutually exclusive.' (exec exits 1, API returns 409). " +
          "Remove this skip once the dump script is fixed.")]
    public async Task MsSql_BackupRestoreBringsBackDroppedTable()
    {
        var api = await KrintApi.GetAsync();
        var version = await api.LatestVersionAsync("mssql");
        await using var instance = await api.ProvisionAsync("mssql", version, "mssql", databases: ["shop"]);
        await Assert.That(instance.Warnings).IsEmpty();

        await KrintApi.EventuallyAsync(() => api.QueryAsync(instance.Id, "shop",
            "CREATE TABLE items (id int PRIMARY KEY, name nvarchar(50) NOT NULL)"), TimeSpan.FromMinutes(2));
        await api.QueryAsync(instance.Id, "shop", "INSERT INTO items (id, name) VALUES (1, N'restored row')");

        var backupId = await api.CreateBackupAsync(instance.Id);
        await api.QueryAsync(instance.Id, "shop", "DROP TABLE items");

        await api.RestoreBackupAsync(backupId);

        var name = await KrintApi.EventuallyAsync(() => api.ScalarAsync(instance.Id, "shop", "SELECT name FROM items WHERE id = 1"), TimeSpan.FromMinutes(2));
        await Assert.That(name).IsEqualTo("restored row");
    }

    private static async Task AssertKeysAsync(KrintApi api, KrintApi.Instance instance)
    {
        instance.ContainerName = await CurrentContainerAsync(api, instance.Id);
        var alpha = await KrintApi.EventuallyAsync(async () =>
        {
            var value = await RedisCli(instance, "GET", "it:alpha");
            return value == "one" ? value : throw new InvalidOperationException($"it:alpha is '{value}'");
        }, TimeSpan.FromSeconds(30));
        await Assert.That(alpha).IsEqualTo("one");
        await Assert.That(await RedisCli(instance, "GET", "it:beta")).IsEqualTo("two");
    }

    private static async Task<string> CurrentContainerAsync(KrintApi api, Guid id) =>
        (await api.GetAsync($"/api/Database/{id}"))["containerName"]!.GetValue<string>();

    private static Task<string> RedisCli(KrintApi.Instance instance, params string[] args) =>
        DockerCli.ExecAsync(instance.ContainerName, ["redis-cli", "-a", instance.Password, "--no-auth-warning", .. args]);
}
