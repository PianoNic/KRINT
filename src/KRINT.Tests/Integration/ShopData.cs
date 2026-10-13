using System.Threading.Tasks;
using TUnit.Core;

namespace KRINT.Tests.Integration;

/// <summary>
/// The Postgres data set the integrity tests carry through backup, visibility and upgrade: an
/// extra database "shop" with 42 rows and a login user "shopapp" granted on it.
/// </summary>
internal static class ShopData
{
    public const string Database = "shop";
    public const string User = "shopapp";
    public const int RowCount = 42;

    public static async Task<KrintApi.Instance> ProvisionPostgresAsync(KrintApi api, string version, string prefix)
    {
        var instance = await api.ProvisionAsync("postgres", version, prefix,
            databases: [Database],
            users: [(User, [Database])]);
        try
        {
            await Assert.That(instance.Warnings).IsEmpty();
            await Assert.That(instance.UserPasswords.ContainsKey(User)).IsTrue();
            await KrintApi.EventuallyAsync(() => api.QueryAsync(instance.Id, Database,
                "CREATE TABLE items (id int PRIMARY KEY, name text NOT NULL)"));
            await api.QueryAsync(instance.Id, Database,
                $"INSERT INTO items SELECT g, 'item ' || g FROM generate_series(1, {RowCount}) g");
            await Assert.That(await CountAsync(api, instance.Id)).IsEqualTo(RowCount);
            return instance;
        }
        catch
        {
            await instance.DisposeAsync();
            throw;
        }
    }

    public static async Task<int> CountAsync(KrintApi api, Guid id) =>
        int.Parse((await KrintApi.EventuallyAsync(() => api.ScalarAsync(id, Database, "SELECT count(*) FROM items")))!);

    /// <summary>Rows, the extra database, the user and the user's password all survived.</summary>
    public static async Task AssertIntactAsync(KrintApi api, KrintApi.Instance instance)
    {
        await Assert.That(await CountAsync(api, instance.Id)).IsEqualTo(RowCount);

        var databases = (await api.GetAsync($"/api/Database/{instance.Id}/databases")).AsArray().Select(n => n!.GetValue<string>()).ToList();
        await Assert.That(databases).Contains(Database);
        var users = (await api.GetAsync($"/api/Database/{instance.Id}/users")).AsArray().Select(n => n!.GetValue<string>()).ToList();
        await Assert.That(users).Contains(User);

        // Operations may swap the container; always ask KRINT which one is current.
        var container = (await api.GetAsync($"/api/Database/{instance.Id}"))["containerName"]!.GetValue<string>();
        var who = await KrintApi.EventuallyAsync(() =>
            Postgres.PsqlAsync(container, User, instance.UserPasswords[User], Database, "SELECT current_user"));
        await Assert.That(who).IsEqualTo(User);
    }
}
