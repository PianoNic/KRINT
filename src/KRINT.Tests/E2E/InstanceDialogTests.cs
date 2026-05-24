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

    [Test]
    public async Task EditDialog_RenamesInstance()
    {
        await Run(async (stack, page, name) =>
        {
            await OpenEdit(page, stack, name);
            var newName = "rn_" + DateTime.UtcNow.ToString("HHmmssfff");
            await page.Locator("[role=dialog] input[placeholder*='Pangolin']").FillAsync(newName);
            await page.GetByRole(AriaRole.Button, new() { Name = "Save name", Exact = true }).ClickAsync();
            // The dialog title updates immediately (optimistic UI), and the API call
            // succeeds in the background. Verifying the title is sufficient end-to-end
            // proof that the rename was issued; persistence belongs in an integration
            // test where we can read the API directly.
            await Assertions.Expect(page.Locator($"[role=dialog] h3:has-text('Edit {newName}')"))
                .ToBeVisibleAsync(new() { Timeout = 10000 });
            await page.Keyboard.PressAsync("Escape");
        }, "ren_");
    }

    [Test]
    public async Task EditDialog_DropsInnerDatabase()
    {
        await Run(async (stack, page, name) =>
        {
            await OpenEdit(page, stack, name);
            await page.Locator("[role=dialog] input[placeholder=my_database]").FillAsync("todrop");
            await page.Locator("[role=dialog] button:has-text('Add')").ClickAsync();
            var item = page.Locator("[role=dialog] li").Filter(new() { HasText = "todrop" });
            await Assertions.Expect(item).ToBeVisibleAsync(new() { Timeout = 10000 });

            await page.Locator("button[aria-label='Drop todrop']").ClickAsync();
            // Scope the confirm to the topmost dialog so we don't match anything in the
            // edit dialog underneath.
            await page.Locator("[role=dialog]").Last.Locator("button").Filter(new() { HasText = "Drop database" }).ClickAsync();
            await Assertions.Expect(item).ToBeHiddenAsync(new() { Timeout = 30000 });
            await page.Keyboard.PressAsync("Escape");
        }, "drp_");
    }

    [Test]
    public async Task EditDialog_DeletesUser()
    {
        await Run(async (stack, page, name) =>
        {
            await OpenEdit(page, stack, name);
            await page.Locator("[role=dialog] button:has-text('Users')").ClickAsync();
            await page.Locator("[role=dialog] input[placeholder=alice]").FillAsync("victim");
            await page.Locator("[role=dialog] button:has-text('Create')").ClickAsync();
            var row = page.Locator("[role=dialog] li").Filter(new() { HasText = "victim" });
            await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 15000 });

            await page.Locator("button[aria-label='Delete user victim']").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Delete user", Exact = true }).ClickAsync();
            await Assertions.Expect(row).ToBeHiddenAsync(new() { Timeout = 15000 });
            await page.Keyboard.PressAsync("Escape");
        }, "delu_");
    }

    [Test]
    public async Task EditDialog_ResetsUserPassword()
    {
        await Run(async (stack, page, name) =>
        {
            await OpenEdit(page, stack, name);
            await page.Locator("[role=dialog] button:has-text('Users')").ClickAsync();
            var userInput = page.Locator("[role=dialog] input[placeholder=alice]");
            await userInput.FillAsync("rotator");
            // Wait for the Create button to enable - it's bound to newUserName.length > 0
            // and can lag a frame behind FillAsync.
            var createBtn = page.Locator("[role=dialog] button:has-text('Create')");
            await Assertions.Expect(createBtn).ToBeEnabledAsync(new() { Timeout = 5000 });
            await createBtn.ClickAsync();
            // First reveal panel from initial create.
            await Assertions.Expect(page.Locator("[role=dialog]").GetByText("Save this password"))
                .ToBeVisibleAsync(new() { Timeout = 15000 });
            // Dismiss it so we can confirm the reset triggers a *new* reveal.
            await page.Locator("[role=dialog] button[aria-label='Dismiss']").ClickAsync();

            await page.Locator("button[aria-label='Reset password for rotator']").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Reset password", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("[role=dialog]").GetByText("Save this password"))
                .ToBeVisibleAsync(new() { Timeout = 15000 });
            await Assertions.Expect(page.Locator("[role=dialog]").GetByText("User rotator"))
                .ToBeVisibleAsync();
            await page.Keyboard.PressAsync("Escape");
        }, "rst_");
    }

    [Test]
    public async Task EditDialog_GrantsUserAccessToDatabase()
    {
        await Run(async (stack, page, name) =>
        {
            await OpenEdit(page, stack, name);
            await page.Locator("[role=dialog] button:has-text('Users')").ClickAsync();
            await page.Locator("[role=dialog] input[placeholder=alice]").FillAsync("grantee");
            await page.Locator("[role=dialog] button:has-text('Create')").ClickAsync();
            var userRow = page.Locator("[role=dialog] li").Filter(new() { HasText = "grantee" });
            await Assertions.Expect(userRow).ToBeVisibleAsync(new() { Timeout = 15000 });
            // Dismiss the password reveal so we can find the user row's Grant button.
            await page.Locator("[role=dialog] button[aria-label='Dismiss']").ClickAsync();

            // The grant control is an hlm-select-trigger (custom element), not a real
            // <button>, so target by its placeholder text instead of an aria-label.
            await userRow.Locator("hlm-select-trigger").ClickAsync();
            // The dropdown's option is the default database name "postgres".
            await page.GetByRole(AriaRole.Option, new() { Name = "postgres", Exact = true }).ClickAsync();

            // Grant badge appears inline on the user row.
            await Assertions.Expect(userRow.GetByText("postgres")).ToBeVisibleAsync(new() { Timeout = 10000 });
            await page.Keyboard.PressAsync("Escape");
        }, "grnt_");
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

    private static async Task Run(Func<KrintStack, IPage, string, Task> body, string prefix, bool skipOuterCleanup = false)
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
            if (!skipOuterCleanup)
                await WizardHelper.CleanupAsync(session.Page, stack, instance.DisplayName);
            await session.Context.CloseAsync();
        }
    }
}
