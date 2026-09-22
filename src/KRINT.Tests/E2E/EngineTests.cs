using System.Threading.Tasks;
using Microsoft.Playwright;
using TUnit.Core;

namespace KRINT.Tests.E2E;

/// <summary>
/// One wizard run per engine family beyond Postgres: the instance comes up, the success screen
/// shows a connection string with the engine's scheme, the row appears, and delete removes it.
/// Engines are picked for image size and boot time; the slow ones (SQL Server, Cassandra,
/// Neo4j) are exercised by the API-level matrix rather than a browser test.
/// </summary>
[NotInParallel("e2e")]
public class EngineTests
{
    [Test]
    [Arguments("MySQL", "mysql://")]
    [Arguments("MariaDB", "mariadb://")]
    [Arguments("MongoDB", "mongodb://")]
    [Arguments("Redis", "redis://")]
    [Arguments("Valkey", "redis://")]
    [Arguments("ClickHouse", "http://")]
    public async Task Engine_ProvisionsThroughTheWizardAndLists(string engineTile, string scheme)
    {
        var stack = await KrintStack.GetAsync();
        var session = await KrintTestFixture.NewAuthenticatedSessionAsync(stack);
        var page = session.Page;
        var instance = await WizardHelper.ProvisionAsync(page, stack, engineTile);
        try
        {
            await Assertions.Expect(page.Locator("code", new() { HasText = scheme })).ToBeVisibleAsync();
            await page.Locator("button:has-text('Go to Instances')").ClickAsync();
            var row = page.Locator("tbody tr").Filter(new() { HasText = instance.DisplayName });
            await Assertions.Expect(row).ToBeVisibleAsync();
            // The row shows a state badge only when the container is NOT running, so a healthy
            // instance is one whose row carries none of the non-running states.
            await Assertions.Expect(row.Locator("text=/exited|created|restarting|dead|paused/i")).ToHaveCountAsync(0);
        }
        finally
        {
            await WizardHelper.CleanupAsync(page, stack, instance.DisplayName);
            await session.Context.CloseAsync();
        }
    }
}
