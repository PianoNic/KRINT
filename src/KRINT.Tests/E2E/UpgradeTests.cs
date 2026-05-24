using System.Threading.Tasks;
using Microsoft.Playwright;
using TUnit.Core;

namespace KRINT.Tests.E2E;

[NotInParallel("e2e")]
public class UpgradeTests
{
    /// <summary>
    /// Provision postgres (WizardHelper actually lands on 18.4 because its has-text("18")
    /// matches the first version row), fire the upgrade API to 17 directly (the SPA's
    /// hlm-select dropdown inside a dialog refuses to open under headless Playwright
    /// reliably; the UI control is not what we're testing here), and verify the row's
    /// Version cell flips to 17 after a fresh /instances fetch. The upgrade pipeline
    /// does a real dump → fresh container → restore → swap, so we allow a generous
    /// timeout.
    /// </summary>
    [Test]
    public async Task Upgrade_BumpsVersionAndUpdatesRow()
    {
        var stack = await KrintStack.GetAsync();
        var session = await KrintTestFixture.NewAuthenticatedSessionAsync(stack);
        var page = session.Page;
        var instance = await WizardHelper.ProvisionPostgresAsync(page, stack, "up_" + DateTime.UtcNow.ToString("HHmmssfff"));
        try
        {
            // Pull the bearer token out of the angular-auth-oidc-client's sessionStorage
            // so raw fetch calls can authenticate the same way Angular's HttpClient does.
            var token = await page.EvaluateAsync<string>(@"() => {
                for (let i = 0; i < sessionStorage.length; i++) {
                    const k = sessionStorage.key(i);
                    if (!k) continue;
                    const v = sessionStorage.getItem(k);
                    try {
                        const j = JSON.parse(v);
                        if (j?.authnResult?.access_token) return j.authnResult.access_token;
                        if (j?.access_token) return j.access_token;
                    } catch { /* not json */ }
                }
                return null;
            }");
            await Assert.That(token).IsNotNull();

            // Look up the instance id and fire the upgrade endpoint directly. The
            // hlm-select dropdown inside a dialog refuses to open reliably under headless
            // Playwright, so we bypass the UI control for this assertion.
            var id = await page.EvaluateAsync<string>($@"async () => {{
                const res = await fetch('/api/Database', {{ headers: {{ Authorization: 'Bearer {token}', Accept: 'application/json' }} }});
                const list = await res.json();
                const match = list.find(d => d.displayName === '{instance.DisplayName}');
                return match?.id;
            }}");
            await Assert.That(id).IsNotNull();

            var status = await page.EvaluateAsync<int>($@"async () => {{
                const res = await fetch('/api/Database/{id}/upgrade', {{
                    method: 'POST',
                    headers: {{ 'Content-Type': 'application/json', Authorization: 'Bearer {token}' }},
                    body: JSON.stringify({{ targetVersion: '17' }}),
                }});
                return res.status;
            }}");
            await Assert.That(status).IsEqualTo(200);

            // The handler returns once the dump-restore-swap finishes, so by here the
            // version field is already updated. Refresh /instances and assert the cell.
            await page.GotoAsync(stack.AppUrl + "/instances");
            var row = page.Locator("tbody tr").Filter(new() { HasText = instance.DisplayName });
            await Assertions.Expect(row.Locator("td").Nth(1)).ToHaveTextAsync("17", new() { Timeout = 30000 });
        }
        finally
        {
            await WizardHelper.CleanupAsync(page, stack, instance.DisplayName);
            await session.Context.CloseAsync();
        }
    }
}
