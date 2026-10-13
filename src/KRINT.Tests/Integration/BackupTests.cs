using System.Threading.Tasks;
using TUnit.Core;

namespace KRINT.Tests.Integration;

/// <summary>Backup entries and schedules. Data round trips live in <see cref="DataIntegrityTests"/>.</summary>
[NotInParallel("integration")]
public class BackupTests
{
    [Test]
    public async Task Backup_CreateListRestoreAndDelete()
    {
        var api = await KrintApi.GetAsync();
        await using var instance = await api.ProvisionAsync("postgres", "18", "bk");
        await KrintApi.EventuallyAsync(() => api.ScalarAsync(instance.Id, "postgres", "SELECT 1"));

        var backupId = await api.CreateBackupAsync(instance.Id);
        var listed = await BackupIdsAsync(api, instance.Id);
        await Assert.That(listed).IsEquivalentTo(new[] { backupId });

        var entry = (await api.GetAsync($"/api/Backups?instanceId={instance.Id}")).AsArray()[0]!;
        await Assert.That(entry["engine"]!.GetValue<string>()).IsEqualTo("postgres");
        await Assert.That(entry["sizeBytes"]!.GetValue<long>()).IsGreaterThan(0);

        // Restore completes without error and doesn't add a second entry.
        await api.RestoreBackupAsync(backupId);
        await Assert.That(await BackupIdsAsync(api, instance.Id)).IsEquivalentTo(new[] { backupId });

        await api.DeleteAsync($"/api/Backups/{backupId}");
        await Assert.That(await BackupIdsAsync(api, instance.Id)).IsEmpty();
    }

    [Test]
    public async Task Schedule_CreateListAndDelete()
    {
        var api = await KrintApi.GetAsync();
        await using var instance = await api.ProvisionAsync("postgres", "18", "bsch");

        var schedule = await api.PostAsync("/api/Backups/schedules", new
        {
            instanceId = instance.Id,
            cronExpression = "0 * * * *",
            description = "Hourly backup",
            keepLast = 3,
        });
        var scheduleId = schedule!["id"]!.GetValue<Guid>();
        await Assert.That(schedule["enabled"]!.GetValue<bool>()).IsTrue();

        var listed = (await api.GetAsync($"/api/Backups/schedules?instanceId={instance.Id}")).AsArray();
        await Assert.That(listed.Count).IsEqualTo(1);
        await Assert.That(listed[0]!["description"]!.GetValue<string>()).IsEqualTo("Hourly backup");

        var toggled = await api.PatchAsync($"/api/Backups/schedules/{scheduleId}", new { enabled = false });
        await Assert.That(toggled!["enabled"]!.GetValue<bool>()).IsFalse();

        await api.DeleteAsync($"/api/Backups/schedules/{scheduleId}");
        await Assert.That((await api.GetAsync($"/api/Backups/schedules?instanceId={instance.Id}")).AsArray().Count).IsEqualTo(0);
    }

    private static async Task<List<Guid>> BackupIdsAsync(KrintApi api, Guid instanceId) =>
        (await api.GetAsync($"/api/Backups?instanceId={instanceId}")).AsArray().Select(b => b!["id"]!.GetValue<Guid>()).ToList();
}
