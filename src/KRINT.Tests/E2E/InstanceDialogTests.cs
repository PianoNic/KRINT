using System.Threading.Tasks;
using Microsoft.Playwright;
using TUnit.Core;

namespace KRINT.Tests.E2E;

[NotInParallel("e2e")]
public class InstanceDialogTests
{
    [Test]
    public async Task ViewDialog_ShowsCredentialsAndConnectionString()
    {
        await Run(async (stack, page, name) =>
        {
            await page.GotoAsync(stack.AppUrl + "/instances");
            var row = page.Locator("tbody tr").Filter(new() { HasText = name });
            await row.Locator("button[aria-label='View details']").ClickAsync();
            await Assertions.Expect(page.Locator("[role=dialog] code").Last).ToBeVisibleAsync(new() { Timeout = 10000 });
            await Assertions.Expect(page.Locator("[role=dialog]").GetByText("Connection string", new() { Exact = true })).ToBeVisibleAsync();
            await page.Keyboard.PressAsync("Escape");
        }, "view_");
    }

    [Test]
    public async Task EditDialog_AddsAndListsInnerDatabase()
    {
        await Run(async (stack, page, name) =>
        {
            await OpenEdit(page, stack, name);
            await page.Locator("[role=dialog] input[placeholder=my_database]").FillAsync("orders");
            await page.Locator("[role=dialog] button:has-text('Add')").ClickAsync();
            await Assertions.Expect(page.Locator("[role=dialog] li:has-text('orders')")).ToBeVisibleAsync(new() { Timeout = 10000 });
            await page.Keyboard.PressAsync("Escape");
        }, "edb_");
    }

    [Test]
    public async Task EditDialog_CreatesUserAndRevealsPasswordOnce()
    {
        await Run(async (stack, page, name) =>
        {
            await OpenEdit(page, stack, name);
            await page.Locator("[role=dialog] button:has-text('Users')").ClickAsync();
            await page.Locator("[role=dialog] input[placeholder=alice]").FillAsync("svcuser");
            await page.Locator("[role=dialog] button:has-text('Create')").ClickAsync();
            await Assertions.Expect(page.Locator("[role=dialog]").GetByText("Save this password"))
                .ToBeVisibleAsync(new() { Timeout = 15000 });
            await Assertions.Expect(page.Locator("[role=dialog] li:has-text('svcuser')")).ToBeVisibleAsync();
            await page.Keyboard.PressAsync("Escape");
        }, "eus_");
    }

    private static async Task OpenEdit(IPage page, KrintStack stack, string displayName)
    {
        await page.GotoAsync(stack.AppUrl + "/instances");
        var row = page.Locator("tbody tr").Filter(new() { HasText = displayName });
        await row.Locator("button[aria-label='More actions']").ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Edit", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator($"[role=dialog] h3:has-text('Edit {displayName}')"))
            .ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    private static async Task Run(Func<KrintStack, IPage, string, Task> body, string prefix)
    {
        var stack = await KrintStack.GetAsync();
        var session = await KrintTestFixture.NewAuthenticatedSessionAsync(stack);
        var instance = await WizardHelper.ProvisionPostgresAsync(session.Page, stack, prefix + DateTime.UtcNow.ToString("HHmmssfff"));
        try
        {
            await body(stack, session.Page, instance.DisplayName);
        }
        finally
        {
            await WizardHelper.CleanupAsync(session.Page, stack, instance.DisplayName);
            await session.Context.CloseAsync();
        }
    }
}
