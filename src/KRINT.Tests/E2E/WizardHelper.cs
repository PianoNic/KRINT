using System.Threading.Tasks;
using Microsoft.Playwright;

namespace KRINT.Tests.E2E;

/// <summary>Drives the /create wizard. Tests own the returned instance and must clean it up.</summary>
internal static class WizardHelper
{
    public record ProvisionedInstance(string ContainerName, string DefaultDb);

    public static async Task<ProvisionedInstance> ProvisionPostgresAsync(IPage page, KrintStack stack, string defaultDbName)
    {
        await page.GotoAsync(stack.AppUrl + "/create");
        await page.Locator("button:has-text('PostgreSQL')").ClickAsync();
        await page.Locator("button:has-text('Next')").ClickAsync();
        await page.Locator("hlm-select-trigger").First.ClickAsync();
        await page.Locator("hlm-select-item:has-text('18')").First.ClickAsync();
        await page.Locator("input[placeholder=postgres]").FillAsync(defaultDbName);
        await page.Locator("button:has-text('Next')").ClickAsync();
        await page.Locator("button:has-text('Next')").ClickAsync();
        await page.Locator("button:has-text('Next')").ClickAsync();
        await page.Locator("button:has-text('Launch')").ClickAsync();
        await Assertions.Expect(page.Locator("text=Instance ready")).ToBeVisibleAsync(new() { Timeout = 120000 });

        var containerName = await page.Locator("code").First.InnerTextAsync();
        return new ProvisionedInstance(containerName.Trim(), defaultDbName);
    }

    public static async Task CleanupAsync(IPage page, KrintStack stack, string containerName)
    {
        try
        {
            page.Dialog += (_, d) => { _ = d.AcceptAsync(); };
            await page.GotoAsync(stack.AppUrl + "/instances");
            var row = page.Locator("tbody tr").Filter(new() { HasText = containerName });
            await row.Locator("button[aria-label='More actions']").ClickAsync(new() { Timeout = 5000 });
            await page.Locator("button:has-text('Delete')").ClickAsync(new() { Timeout = 5000 });
            await Assertions.Expect(row).ToBeHiddenAsync(new() { Timeout = 30000 });
        }
        catch
        {
            // Don't mask the real test failure with cleanup noise.
        }
    }
}
