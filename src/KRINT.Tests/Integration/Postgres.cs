using System.Threading.Tasks;

namespace KRINT.Tests.Integration;

/// <summary>
/// psql inside a provisioned Postgres container. The official image trusts the socket and
/// 127.0.0.1, so a loopback login proves nothing about a password; connecting to the
/// container's own network address goes through scram-sha-256, so a successful call proves the
/// user's credentials work.
/// </summary>
internal static class Postgres
{
    public static async Task<string> PsqlAsync(string container, string user, string password, string database, string sql)
    {
        var host = await NetworkAddressAsync(container);
        return await DockerCli.RunCheckedAsync("exec", "-e", "PGPASSWORD=" + password, container,
            "psql", "-h", host, "-U", user, "-d", database, "-tAc", sql);
    }

    public static async Task<bool> CanLoginAsync(string container, string user, string password, string database)
    {
        var host = await NetworkAddressAsync(container);
        var result = await DockerCli.RunAsync("exec", "-e", "PGPASSWORD=" + password, container,
            "psql", "-h", host, "-U", user, "-d", database, "-tAc", "SELECT 1");
        return result.ExitCode == 0 && result.Out == "1";
    }

    private static async Task<string> NetworkAddressAsync(string container)
    {
        var addresses = await DockerCli.RunCheckedAsync("inspect", "-f",
            "{{range .NetworkSettings.Networks}}{{.IPAddress}} {{end}}", container);
        return addresses.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? throw new InvalidOperationException($"{container} has no network address");
    }
}
