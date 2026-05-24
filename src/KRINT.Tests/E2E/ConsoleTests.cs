using System.Threading.Tasks;
using Microsoft.Playwright;
using TUnit.Core;

namespace KRINT.Tests.E2E;

[NotInParallel("e2e")]
public class ConsoleTests
{
    /// <summary>
    /// Pick an instance, switch to the Exec tab, type a marker echo, and confirm the
    /// SignalR exec stream pipes the output back into the in-page terminal.
    /// </summary>
    [Test]
    public async Task Console_ExecsCommandAndStreamsOutput()
    {
        var stack = await KrintStack.GetAsync();
        var session = await KrintTestFixture.NewAuthenticatedSessionAsync(stack);
        var page = session.Page;
        var instance = await WizardHelper.ProvisionPostgresAsync(page, stack, "cns_" + DateTime.UtcNow.ToString("HHmmssfff"));
        try
        {
            await page.GotoAsync(stack.AppUrl + "/console");
            await page.Locator("aside button").Filter(new() { HasText = instance.DisplayName }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Exec", Exact = true }).ClickAsync();

            // xterm uses a hidden helper textarea (.xterm-helper-textarea) to absorb
            // keystrokes - it's not Playwright-visible. Click the visible terminal viewport
            // to give it focus, then type via page.Keyboard.
            // Wait for the SignalR exec channel to actually open before typing - the OPEN
            // badge surfaces this in the UI; without it, keystrokes hit a dead socket.
            await Assertions.Expect(page.Locator("main").GetByText("OPEN").First)
                .ToBeVisibleAsync(new() { Timeout = 20000 });
            // xterm uses an opacity-0 helper <textarea> to absorb keystrokes - Playwright
            // refuses to click it (invisible). Focus it via JS, then dispatch real
            // keystrokes with page.Keyboard so they flow through the SignalR exec channel.
            await page.EvaluateAsync("() => document.querySelector('.xterm-helper-textarea').focus()");
            await page.Keyboard.TypeAsync("echo krint_e2e_marker");
            await page.Keyboard.PressAsync("Enter");

            // xterm renders text into many spans; scope to <main> and match substring.
            await Assertions.Expect(page.Locator("main").GetByText("krint_e2e_marker").First)
                .ToBeVisibleAsync(new() { Timeout = 15000 });
        }
        finally
        {
            await WizardHelper.CleanupAsync(page, stack, instance.DisplayName);
            await session.Context.CloseAsync();
        }
    }
}
