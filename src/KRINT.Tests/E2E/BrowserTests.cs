using System.Threading.Tasks;
using Microsoft.Playwright;
using TUnit.Core;

namespace KRINT.Tests.E2E;

[NotInParallel("e2e")]
public class BrowserTests
{
    /// <summary>
    /// Pick an instance, type a CREATE TABLE in the Query tab, run it, switch back to
    /// the Data tab, and verify the new table shows up in the Entities sidebar. Covers
    /// the run-query + entity-refresh code paths in one shot.
    /// </summary>
    [Test]
    public async Task Browser_RunsCreateTableAndShowsItInSidebar()
    {
        var stack = await KrintStack.GetAsync();
        var session = await KrintTestFixture.NewAuthenticatedSessionAsync(stack);
        var page = session.Page;
        var instance = await WizardHelper.ProvisionPostgresAsync(page, stack, "brw_" + DateTime.UtcNow.ToString("HHmmssfff"));
        try
        {
            await page.GotoAsync(stack.AppUrl + "/browser");
            await page.Locator("aside button").Filter(new() { HasText = instance.DisplayName }).ClickAsync();

            await page.GetByRole(AriaRole.Button, new() { Name = "Query", Exact = true }).ClickAsync();
            // CodeMirror's contenteditable doesn't respond to Fill; focus + execCommand
            // is the cheapest cross-extension way to push text in.
            await page.Locator(".cm-content").ClickAsync();
            await page.EvaluateAsync(@"() => {
                document.querySelector('.cm-content').focus();
                document.execCommand('insertText', false, 'CREATE TABLE e2e_browser_test (id int);');
            }");
            await page.GetByRole(AriaRole.Button, new() { Name = "Run query", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByText("0 rows affected"))
                .ToBeVisibleAsync(new() { Timeout = 15000 });

            await page.GetByRole(AriaRole.Button, new() { Name = "Data", Exact = true }).ClickAsync();
            await page.Locator("button[aria-label='Refresh instances, databases, and entities']").ClickAsync();
            await Assertions.Expect(page.Locator("aside").GetByText("e2e_browser_test"))
                .ToBeVisibleAsync(new() { Timeout = 15000 });
        }
        finally
        {
            await WizardHelper.CleanupAsync(page, stack, instance.DisplayName);
            await session.Context.CloseAsync();
        }
    }
}
