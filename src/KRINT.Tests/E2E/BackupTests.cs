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
        var stack = await KrintStack.GetAsync();
        var session = await KrintTestFixture.NewAuthenticatedSessionAsync(stack);
        var page = session.Page;
        var instance = await WizardHelper.ProvisionPostgresAsync(page, stack, "bk_" + DateTime.UtcNow.ToString("HHmmssfff"));
        try
        {
            await page.GotoAsync(stack.AppUrl + "/backups");
            // Scope to the instance via the left sidebar (instance picker).
            await page.Locator("aside button").Filter(new() { HasText = instance.DisplayName }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Create backup", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("tbody tr").First)
                .ToBeVisibleAsync(new() { Timeout = 60000 });
        }
        finally
        {
            await WizardHelper.CleanupAsync(page, stack, instance.DisplayName);
            await session.Context.CloseAsync();
        }
    }
}
