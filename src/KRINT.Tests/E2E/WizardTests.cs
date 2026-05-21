using System.Threading.Tasks;
using Microsoft.Playwright;
using TUnit.Core;

namespace KRINT.Tests.E2E;

[NotInParallel("e2e")]
public class WizardTests
{
    [Test]
    public async Task Wizard_ProvisionsPostgresInstance()
    {
        var stack = await KrintStack.GetAsync();
        var session = await KrintTestFixture.NewAuthenticatedSessionAsync(stack);
        var page = session.Page;
        var instance = await WizardHelper.ProvisionPostgresAsync(page, stack, "wiz_" + Suffix());
        try
        {
            await Assertions.Expect(page.Locator("text=Instance ready")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("code", new() { HasText = "postgres://" })).ToBeVisibleAsync();
            await page.Locator("button:has-text('Go to Instances')").ClickAsync();
            await Assertions.Expect(
                page.Locator("tbody tr").Filter(new() { HasText = instance.ContainerName })
            ).ToBeVisibleAsync();
        }
        finally
        {
            await WizardHelper.CleanupAsync(page, stack, instance.ContainerName);
            await session.Context.CloseAsync();
        }
    }

    [Test]
    public async Task Wizard_DefaultDbNameAppearsInRow()
    {
        var stack = await KrintStack.GetAsync();
        var session = await KrintTestFixture.NewAuthenticatedSessionAsync(stack);
        var page = session.Page;
        var dbName = "named_" + Suffix();
        var instance = await WizardHelper.ProvisionPostgresAsync(page, stack, dbName);
        try
        {
            await page.Locator("button:has-text('Go to Instances')").ClickAsync();
            var row = page.Locator("tbody tr").Filter(new() { HasText = instance.ContainerName });
            await Assertions.Expect(row).ToContainTextAsync(dbName);
        }
        finally
        {
            await WizardHelper.CleanupAsync(page, stack, instance.ContainerName);
            await session.Context.CloseAsync();
        }
    }

    private static string Suffix() => DateTime.UtcNow.ToString("HHmmssfff");
}
