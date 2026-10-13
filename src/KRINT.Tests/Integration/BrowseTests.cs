using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using TUnit.Core;

namespace KRINT.Tests.Integration;

/// <summary>The data browser and the console: query, list tables, read rows, exec.</summary>
[NotInParallel("integration")]
public class BrowseTests
{
    [Test]
    public async Task Query_CreatesTableThatListsAndReturnsRows()
    {
        var api = await KrintApi.GetAsync();
        await using var instance = await api.ProvisionAsync("postgres", "18", "brw");

        var created = await KrintApi.EventuallyAsync(() =>
            api.QueryAsync(instance.Id, "postgres", "CREATE TABLE it_browser_test (id int, label text)"));
        await Assert.That(created["rowsAffected"]!.GetValue<int>()).IsEqualTo(0);

        var inserted = await api.QueryAsync(instance.Id, "postgres", "INSERT INTO it_browser_test VALUES (1, 'one'), (2, 'two')");
        await Assert.That(inserted["rowsAffected"]!.GetValue<int>()).IsEqualTo(2);

        var tables = (await api.GetAsync($"/api/Database/{instance.Id}/browse/postgres/tables")).AsArray();
        var table = tables.Select(t => t!["name"]!.GetValue<string>()).FirstOrDefault(n => n.EndsWith("it_browser_test"));
        await Assert.That(table).IsNotNull();

        var rows = await api.GetAsync($"/api/Database/{instance.Id}/browse/postgres/tables/{Uri.EscapeDataString(table!)}/rows");
        var columns = rows["columns"]!.AsArray().Select(c => c!.GetValue<string>()).ToList();
        await Assert.That(columns).Contains("id");
        await Assert.That(columns).Contains("label");
        await Assert.That(rows["rows"]!.AsArray().Count).IsEqualTo(2);

        var select = await api.QueryAsync(instance.Id, "postgres", "SELECT label FROM it_browser_test ORDER BY id");
        var labels = select["rows"]!.AsArray().Select(r => r![0]!.GetValue<string>()).ToList();
        await Assert.That(labels).IsEquivalentTo(new[] { "one", "two" });
    }

    [Test]
    public async Task Query_ReportsSqlErrorsAsBadRequest()
    {
        var api = await KrintApi.GetAsync();
        await using var instance = await api.ProvisionAsync("postgres", "18", "qerr");

        await KrintApi.EventuallyAsync(() => api.ScalarAsync(instance.Id, "postgres", "SELECT 1"));
        using var res = await api.SendAsync(System.Net.Http.HttpMethod.Post, $"/api/Database/{instance.Id}/query",
            new { database = "postgres", sql = "SELECT * FROM no_such_table" });
        await Assert.That((int)res.StatusCode).IsEqualTo(400);
        await Assert.That(await res.Content.ReadAsStringAsync()).Contains("no_such_table");
    }

    /// <summary>The console's exec channel pipes shell output back over SignalR.</summary>
    [Test]
    public async Task Console_ExecStreamsCommandOutput()
    {
        var api = await KrintApi.GetAsync();
        await using var instance = await api.ProvisionAsync("postgres", "18", "cns");

        await using var hub = await HubClient.ConnectAsync(api, "container");
        var output = new System.Text.StringBuilder();
        var marker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<string, string>("ExecOutput", (_, base64) =>
        {
            lock (output)
            {
                output.Append(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
                // The terminal echoes the typed command too; the marker is only complete in the
                // command's own output, where the two halves are joined.
                if (output.ToString().Contains("krint_it_marker")) marker.TrySetResult();
            }
        });

        var sessionId = await hub.InvokeAsync<string>("StartExec", instance.Id, 120u, 30u);
        var command = "echo krint_it_\"\"marker\n";
        await hub.InvokeAsync("WriteExec", sessionId, Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(command)));

        var finished = await Task.WhenAny(marker.Task, Task.Delay(TimeSpan.FromSeconds(20)));
        string seen;
        lock (output) seen = output.ToString();
        await Assert.That(finished == marker.Task).IsTrue().Because($"exec output was: {seen}");
        await hub.InvokeAsync("EndExec", sessionId);
    }
}
