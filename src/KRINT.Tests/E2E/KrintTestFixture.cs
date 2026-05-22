using System.Threading.Tasks;
using Microsoft.Playwright;

namespace KRINT.Tests.E2E;

/// <summary>
/// Per-test helpers on top of the session-scoped <see cref="KrintStack"/>.
/// Each test creates its own browser context and does its own login - fast enough
/// (~3s) and free of cross-test sessionStorage races.
/// </summary>
public static class KrintTestFixture
{
    public static bool Headless { get; set; } = true;

    private static IPlaywright? _playwright;
    private static IBrowser? _browser;
    private static readonly SemaphoreSlim BrowserGate = new(1, 1);

    public static async Task<IBrowser> GetBrowserAsync()
    {
        await BrowserGate.WaitAsync();
        try
        {
            if (_browser is not null) return _browser;
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new()
            {
                Headless = Headless,
                SlowMo = Headless ? 0 : 50,
            });
            return _browser;
        }
        finally
        {
            BrowserGate.Release();
        }
    }

    public static async Task DisposeAsync()
    {
        try { if (_browser is not null) await _browser.DisposeAsync(); } catch { }
        try { _playwright?.Dispose(); } catch { }
        _browser = null;
        _playwright = null;
    }

    public sealed record Session(IBrowserContext Context, IPage Page);

    /// <summary>
    /// Opens a fresh browser context and signs in via Keycloak using the realm-seeded
    /// e2e_runner account. Returns once the Angular sidenav has rendered.
    /// </summary>
    public static async Task<Session> NewAuthenticatedSessionAsync(KrintStack stack)
    {
        var browser = await GetBrowserAsync();
        var ctx = await browser.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync(stack.AppUrl + "/");

        await page.GetByRole(AriaRole.Textbox, new() { Name = "Username or email" }).FillAsync(stack.TestUsername);
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Password", Exact = true }).FillAsync(stack.TestPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign In" }).ClickAsync();

        await page.WaitForURLAsync(u => u.StartsWith(stack.AppUrl), new() { Timeout = 15000 });
        await Assertions.Expect(page.Locator("a[href='/instances']")).ToBeVisibleAsync(new() { Timeout = 15000 });
        return new Session(ctx, page);
    }
}
