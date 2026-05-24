using System.Threading.Tasks;
using Microsoft.Playwright;
using TUnit.Core;

namespace KRINT.Tests.E2E;

[NotInParallel("e2e")]
public class BackupTests
{
    [Test]
    public async Task BackupNow_CreatesBackupRowForInstance()
    {
        await WithInstance("bk_", async (page, instance) =>
        {
            await page.GotoAsync($"{(await KrintStack.GetAsync()).AppUrl}/backups");
            await ScopeToInstance(page, instance.DisplayName);
            await page.GetByRole(AriaRole.Button, new() { Name = "Create backup", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("tbody tr").First)
                .ToBeVisibleAsync(new() { Timeout = 60000 });
        });
    }

    [Test]
    public async Task BackupRestore_CompletesWithoutError()
    {
        await WithInstance("brst_", async (page, instance) =>
        {
            var stack = await KrintStack.GetAsync();
            await page.GotoAsync(stack.AppUrl + "/backups");
            await ScopeToInstance(page, instance.DisplayName);
            await page.GetByRole(AriaRole.Button, new() { Name = "Create backup", Exact = true }).ClickAsync();
            var backupRow = page.Locator("tbody tr").First;
            await Assertions.Expect(backupRow).ToBeVisibleAsync(new() { Timeout = 60000 });

            var restoreBtn = backupRow.Locator("button[aria-label='Restore from this backup']");
            await restoreBtn.ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Restore", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("[role=dialog]")).ToBeHiddenAsync(new() { Timeout = 60000 });

            // SPA disables the row's Restore button while restoring() === entry.id. Wait
            // for it to re-enable - that's the "restore completed" signal, success or
            // failure. If it failed, an error banner would appear too.
            await Assertions.Expect(restoreBtn).ToBeEnabledAsync(new() { Timeout = 90000 });
            await Assertions.Expect(page.Locator("main .text-destructive")).ToHaveCountAsync(0);
            // Restore doesn't duplicate the backup row.
            await Assertions.Expect(page.Locator("tbody tr")).ToHaveCountAsync(1);
        });
    }

    [Test]
    public async Task BackupDelete_RemovesRowFromList()
    {
        await WithInstance("bdel_", async (page, instance) =>
        {
            var stack = await KrintStack.GetAsync();
            await page.GotoAsync(stack.AppUrl + "/backups");
            await ScopeToInstance(page, instance.DisplayName);
            await page.GetByRole(AriaRole.Button, new() { Name = "Create backup", Exact = true }).ClickAsync();
            var row = page.Locator("tbody tr").First;
            await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 60000 });

            await row.Locator("button[aria-label='Delete backup']").ClickAsync();
            // The confirm modal's primary button is just labelled "Delete".
            await page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
            await Assertions.Expect(row).ToBeHiddenAsync(new() { Timeout = 15000 });
        });
    }

    [Test]
    public async Task BackupSchedule_CreatesAndDeletesPresetSchedule()
    {
        await WithInstance("bsch_", async (page, instance) =>
        {
            var stack = await KrintStack.GetAsync();
            await page.GotoAsync(stack.AppUrl + "/backups");
            await ScopeToInstance(page, instance.DisplayName);

            await page.GetByRole(AriaRole.Button, new() { Name = "New schedule", Exact = true }).ClickAsync();
            // The dialog presets are buttons whose label starts with the cadence name.
            await page.Locator("[role=dialog] button").Filter(new() { HasTextRegex = new System.Text.RegularExpressions.Regex("^Every hour") }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Create schedule", Exact = true }).ClickAsync();

            var scheduleRow = page.Locator("tbody tr").Filter(new() { HasText = "Hourly backup" });
            await Assertions.Expect(scheduleRow).ToBeVisibleAsync(new() { Timeout = 15000 });

            await scheduleRow.Locator("button[aria-label='Delete schedule']").ClickAsync();
            // The confirm modal's primary button text is "Delete schedule" - scope to the
            // dialog so we don't also match the row's icon button (same aria-label).
            await page.Locator("[role=dialog] button").Filter(new() { HasText = "Delete schedule" }).ClickAsync();
            await Assertions.Expect(scheduleRow).ToBeHiddenAsync(new() { Timeout = 15000 });
        });
    }

    private static async Task ScopeToInstance(IPage page, string displayName)
    {
        await page.Locator("aside button").Filter(new() { HasText = displayName }).ClickAsync();
    }

    private static async Task WithInstance(string prefix, Func<IPage, WizardHelper.ProvisionedInstance, Task> body)
    {
        var stack = await KrintStack.GetAsync();
        var session = await KrintTestFixture.NewAuthenticatedSessionAsync(stack);
        var page = session.Page;
        var instance = await WizardHelper.ProvisionPostgresAsync(page, stack, prefix + DateTime.UtcNow.ToString("HHmmssfff"));
        try
        {
            await body(page, instance);
        }
        finally
        {
            await WizardHelper.CleanupAsync(page, stack, instance.DisplayName);
            await session.Context.CloseAsync();
        }
    }
}
