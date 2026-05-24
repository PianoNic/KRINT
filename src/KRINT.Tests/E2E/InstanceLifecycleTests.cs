using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Playwright;
using TUnit.Core;

namespace KRINT.Tests.E2E;

[NotInParallel("e2e")]
public class InstanceLifecycleTests
{
    [Test]
    public async Task Delete_RemovesRowAndDockerContainer()
    {
        var stack = await KrintStack.GetAsync();
        var session = await KrintTestFixture.NewAuthenticatedSessionAsync(stack);
        var page = session.Page;
        var instance = await WizardHelper.ProvisionPostgresAsync(page, stack, "del_" + DateTime.UtcNow.ToString("HHmmssfff"));
        try
        {
            await page.GotoAsync(stack.AppUrl + "/instances");
            var row = page.Locator("tbody tr").Filter(new() { HasText = instance.DisplayName });
            await row.Locator("button[aria-label='More actions']").ClickAsync();
            await page.GetByRole(AriaRole.Menuitem, new() { Name = "Delete", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Delete instance", Exact = true }).ClickAsync();
            await Assertions.Expect(row).ToBeHiddenAsync(new() { Timeout = 30000 });

            var psi = new ProcessStartInfo("docker", $"ps -a --filter name={instance.ContainerName} --format {{{{.Names}}}}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            var stdout = (await p.StandardOutput.ReadToEndAsync()).Trim();
            await Assert.That(stdout).IsEqualTo(string.Empty);
        }
        finally
        {
            await session.Context.CloseAsync();
        }
    }
}
