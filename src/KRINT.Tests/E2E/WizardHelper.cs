using System.Threading.Tasks;
using Microsoft.Playwright;

namespace KRINT.Tests.E2E;

/// <summary>Drives the /create wizard. Tests own the returned instance and must clean it up.</summary>
internal static class WizardHelper
{
    /// <summary>
    /// DisplayName is the user-supplied name shown across the UI (rows, dialogs, sections).
    /// ContainerName is the actual Docker container name (e.g. krint-pg-xxxx), used for
    /// docker CLI assertions.
    /// </summary>
    public record ProvisionedInstance(string DisplayName, string ContainerName, string DefaultDb);

    /// <summary>
    /// Drives the wizard for any catalog engine with its defaults: the newest version is
    /// preselected, the suggested name is replaced by a unique one, and Next is pressed until
    /// Launch appears (the Databases step is hidden for engines without logical databases, so
    /// the number of steps varies).
    /// </summary>
    public static async Task<ProvisionedInstance> ProvisionAsync(IPage page, KrintStack stack, string engineTile, int readyTimeoutMs = 240000)
    {
        var instanceName = "Test_" + DateTime.UtcNow.ToString("HHmmssfff");

        await page.GotoAsync(stack.AppUrl + "/create");
        await page.Locator($"button:has-text('{engineTile}')").First.ClickAsync();
        await page.Locator("button:has-text('Next')").ClickAsync();
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Name", Exact = true }).FillAsync(instanceName);

        // Angular swaps Next for Launch on the Review step a tick after the click, so give the
        // render a moment before looking; otherwise the loop asks for a Next that is gone.
        for (var i = 0; i < 6; i++)
        {
            await page.WaitForTimeoutAsync(250);
            if (await page.Locator("button:has-text('Launch')").CountAsync() > 0) break;
            await page.Locator("button:has-text('Next')").ClickAsync(new() { Timeout = 10000 });
        }

        await page.Locator("button:has-text('Launch')").ClickAsync();
        await Assertions.Expect(page.Locator("text=Instance ready")).ToBeVisibleAsync(new() { Timeout = readyTimeoutMs });

        var containerName = (await page.Locator("code").First.InnerTextAsync()).Trim();
        return new ProvisionedInstance(instanceName, containerName, string.Empty);
    }

    public static async Task<ProvisionedInstance> ProvisionPostgresAsync(IPage page, KrintStack stack, string defaultDbName)
    {
        var instanceName = "Test_" + DateTime.UtcNow.ToString("HHmmssfff");

        await page.GotoAsync(stack.AppUrl + "/create");
        // Step 1: Engine.
        await page.Locator("button:has-text('PostgreSQL')").ClickAsync();
        await page.Locator("button:has-text('Next')").ClickAsync();
        // Step 2: Basics (version + required name + optional default DB).
        await page.Locator("hlm-select-trigger").First.ClickAsync();
        await page.Locator("hlm-select-item:has-text('18')").First.ClickAsync();
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Name", Exact = true }).FillAsync(instanceName);
        await page.Locator("input[placeholder=postgres]").FillAsync(defaultDbName);
        await page.Locator("button:has-text('Next')").ClickAsync();
        // Steps 3-5: Plugins / Databases / Users (defaults are fine).
        await page.Locator("button:has-text('Next')").ClickAsync();
        await page.Locator("button:has-text('Next')").ClickAsync();
        await page.Locator("button:has-text('Next')").ClickAsync();
        // Step 6: Review.
        await page.Locator("button:has-text('Launch')").ClickAsync();
        await Assertions.Expect(page.Locator("text=Instance ready")).ToBeVisibleAsync(new() { Timeout = 120000 });

        var containerName = (await page.Locator("code").First.InnerTextAsync()).Trim();
        return new ProvisionedInstance(instanceName, containerName, defaultDbName);
    }

    public static async Task CleanupAsync(IPage page, KrintStack stack, string displayName)
    {
        try
        {
            await page.GotoAsync(stack.AppUrl + "/instances");
            var row = page.Locator("tbody tr").Filter(new() { HasText = displayName });
            await row.Locator("button[aria-label='More actions']").ClickAsync(new() { Timeout = 5000 });
            // The "Delete" item in the menu opens a confirm dialog with its own
            // "Delete instance" button. The menu's "Delete" is exact; the confirm's text
            // is "Delete instance" - select on exact-match to avoid both flowing into
            // strict-mode violations.
            await page.GetByRole(AriaRole.Menuitem, new() { Name = "Delete", Exact = true }).ClickAsync(new() { Timeout = 5000 });
            await page.GetByRole(AriaRole.Button, new() { Name = "Delete instance", Exact = true }).ClickAsync(new() { Timeout = 5000 });
            await Assertions.Expect(row).ToBeHiddenAsync(new() { Timeout = 30000 });
        }
        catch
        {
            // Don't mask the real test failure with cleanup noise.
        }
    }
}
