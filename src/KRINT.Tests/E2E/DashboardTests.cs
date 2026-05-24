using System.Threading.Tasks;
using Microsoft.Playwright;
using TUnit.Core;

namespace KRINT.Tests.E2E;

[NotInParallel("e2e")]
public class DashboardTests
{
    /// <summary>
    /// After provisioning, the home dashboard's "Instances" KPI should pick up the new
    /// instance (via the SignalR push, or at worst the next poll) and the recent
    /// activity list should include the instance.create event for it.
    /// </summary>
    [Test]
    public async Task Dashboard_ReflectsNewlyProvisionedInstance()
    {
        var stack = await KrintStack.GetAsync();
        var session = await KrintTestFixture.NewAuthenticatedSessionAsync(stack);
        var page = session.Page;

        await page.GotoAsync(stack.AppUrl + "/");
        var instancesCard = page.Locator("article").Filter(new() { HasText = "Instances" }).First;
        var beforeText = (await instancesCard.InnerTextAsync()).Trim();
        var before = ParseLeadingInt(beforeText.Split('\n').Last().Trim());

        var instance = await WizardHelper.ProvisionPostgresAsync(page, stack, "dash_" + DateTime.UtcNow.ToString("HHmmssfff"));
        try
        {
            await page.GotoAsync(stack.AppUrl + "/");
            await Assertions.Expect(instancesCard.Locator(".text-2xl"))
                .ToHaveTextAsync((before + 1).ToString(), new() { Timeout = 15000 });

            // "Recent activity" lives in a <section>, not <article>; rows show the
            // instance's container name in a .truncate <span>.
            var recentActivity = page.Locator("section").Filter(new() { HasText = "Recent activity" });
            await Assertions.Expect(recentActivity.GetByText(instance.ContainerName).First)
                .ToBeVisibleAsync(new() { Timeout = 15000 });
        }
        finally
        {
            await WizardHelper.CleanupAsync(page, stack, instance.DisplayName);
            await session.Context.CloseAsync();
        }
    }

    private static int ParseLeadingInt(string s) => int.TryParse(new string(s.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0;
}
