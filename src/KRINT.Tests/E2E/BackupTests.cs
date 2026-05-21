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
            var section = page.Locator(".rounded-md.border.p-4").Filter(new() { HasText = instance.ContainerName });
            await section.Locator("button:has-text('Backup now')").ClickAsync();
            await Assertions.Expect(section.Locator($"tbody tr:has-text('{instance.ContainerName}')").First)
                .ToBeVisibleAsync(new() { Timeout = 60000 });
        }
        finally
        {
            await WizardHelper.CleanupAsync(page, stack, instance.ContainerName);
            await session.Context.CloseAsync();
        }
    }
}
