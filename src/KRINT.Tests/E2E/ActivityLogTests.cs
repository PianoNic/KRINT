using System.Threading.Tasks;
using Microsoft.Playwright;
using TUnit.Core;

namespace KRINT.Tests.E2E;

[NotInParallel("e2e")]
public class ActivityLogTests
{
    [Test]
    public async Task Activity_RecordsInstanceCreate()
    {
        var stack = await KrintStack.GetAsync();
        var session = await KrintTestFixture.NewAuthenticatedSessionAsync(stack);
        var page = session.Page;
        var instance = await WizardHelper.ProvisionPostgresAsync(page, stack, "act_" + DateTime.UtcNow.ToString("HHmmssfff"));
        try
        {
            await page.GotoAsync(stack.AppUrl + "/activity");
            // Activity rows reference instances by their docker container name, not the display name.
            var row = page.Locator("tbody tr")
                .Filter(new() { HasText = instance.ContainerName })
                .Filter(new() { HasText = "instance.create" });
            await Assertions.Expect(row.First).ToBeVisibleAsync(new() { Timeout = 10000 });
        }
        finally
        {
            await WizardHelper.CleanupAsync(page, stack, instance.DisplayName);
            await session.Context.CloseAsync();
        }
    }
}
