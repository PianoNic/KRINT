using System.Threading.Tasks;
using Microsoft.Playwright;
using TUnit.Core;

namespace KRINT.Tests.E2E;

[NotInParallel("e2e")]
public class NavigationTests
{
    [Test]
    public async Task Sidenav_RendersAllSections()
    {
        var stack = await KrintStack.GetAsync();
        var session = await KrintTestFixture.NewAuthenticatedSessionAsync(stack);
        try
        {
            foreach (var route in new[] { "/instances", "/browser", "/backups", "/activity", "/settings", "/create" })
            {
                await session.Page.GotoAsync(stack.AppUrl + route);
                await Assertions.Expect(session.Page.Locator("main").First).ToBeVisibleAsync(new() { Timeout = 5000 });
            }
        }
        finally
        {
            await session.Context.CloseAsync();
        }
    }

    [Test]
    public async Task SettingsPage_ShowsPortRangesAndEngines()
    {
        var stack = await KrintStack.GetAsync();
        var session = await KrintTestFixture.NewAuthenticatedSessionAsync(stack);
        try
        {
            await session.Page.GotoAsync(stack.AppUrl + "/settings");
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Port ranges" })).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Supported engines" })).ToBeVisibleAsync();
        }
        finally
        {
            await session.Context.CloseAsync();
        }
    }
}
