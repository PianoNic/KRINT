using System.Threading.Tasks;
using Microsoft.Playwright;
using TUnit.Core;

namespace KRINT.Tests.E2E;

[NotInParallel("e2e")]
public class MigrationTests
{
    /// <summary>
    /// Validates the guided-migration wizard's discover-to-stream path:
    ///   1. A compose-labelled source container is offered as a candidate in the Register
    ///      dialog (validates PR #115's compose-label discovery).
    ///   2. The "Migrate into KRINT" button gates correctly on compose-managed + supported
    ///      engine + running (validates PR #117's gating).
    ///   3. The wizard opens, the review form pre-fills from the candidate's metadata, and
    ///      "Start migration" transitions the wizard into its progress panel (validates the
    ///      MigrationHub stream from PR #116 actually accepts the request).
    ///
    /// We deliberately don't assert "Migration complete" here: the KRINT container running
    /// inside compose can't reach the source container's host-published port using the
    /// "localhost" host that discovery returns - that's a separate connectivity issue
    /// tracked as its own bug (KRINT-in-container -&gt; source-on-host requires either
    /// host.docker.internal substitution or shared docker network). Once that's resolved,
    /// extend this test to assert success and the new managed instance row.
    /// </summary>
    [Test]
    public async Task MigrationWizard_DiscoversComposeSource_AndStartsStream()
    {
        var stack = await KrintStack.GetAsync();
        await using var source = await MigrationSourceContainer.StartPostgresAsync();
        var session = await KrintTestFixture.NewAuthenticatedSessionAsync(stack);
        var page = session.Page;
        try
        {
            await page.GotoAsync(stack.AppUrl + "/instances");

            // Open the Register external dialog - same entry point a user would use.
            await page.GetByRole(AriaRole.Button, new() { Name = "Add external", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Scan", Exact = true }).ClickAsync();

            // The discovery query lists every untracked DB container; filter to ours by name.
            var candidateRow = page.Locator("li").Filter(new() { HasText = source.ContainerName });
            await Assertions.Expect(candidateRow).ToBeVisibleAsync(new() { Timeout = 30000 });

            // Migrate button is the headline gating assertion - it only appears for compose +
            // running + supported engine. PR 1 and PR 3 working together.
            var migrateBtn = candidateRow.GetByRole(AriaRole.Button, new() { Name = "Migrate", Exact = true });
            await Assertions.Expect(migrateBtn).ToBeVisibleAsync();
            await migrateBtn.ClickAsync();

            // Wizard dialog. Review pane shows the source container name in its description.
            var dialog = page.Locator("[role=dialog]").Filter(new() { HasText = "Migrate into KRINT" });
            await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5000 });

            // Defaults: name = `<compose-service>-krint`, source password came from POSTGRES_PASSWORD,
            // source database from POSTGRES_DB. Just sanity-check the wizard pulled them through.
            await Assertions.Expect(dialog.Locator("#mig-name")).ToHaveValueAsync(source.ComposeService + "-krint");
            await Assertions.Expect(dialog.Locator("#mig-db")).ToHaveValueAsync(source.Database);

            // Kick off the stream. We expect the wizard to switch to its progress panel.
            // "running" or "failed" is acceptable here - we're validating the stream connects,
            // not the migration outcome (see test summary for the connectivity caveat).
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Start migration", Exact = true }).ClickAsync();
            var progressOrError = dialog.Locator("text=/Migrating|Migration failed|Migration complete/");
            await Assertions.Expect(progressOrError).ToBeVisibleAsync(new() { Timeout = 30000 });
        }
        finally
        {
            await session.Context.CloseAsync();
        }
    }
}
