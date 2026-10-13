using System.Threading.Tasks;

namespace KRINT.Tests.Integration;

/// <summary>
/// psql inside a provisioned Postgres container. Connecting over 127.0.0.1 (not the socket)
/// goes through password auth, so a successful call proves the user's credentials work.
/// </summary>
internal static class Postgres
{
    public static Task<string> PsqlAsync(string container, string user, string password, string database, string sql) =>
        DockerCli.RunCheckedAsync("exec", "-e", "PGPASSWORD=" + password, container,
            "psql", "-h", "127.0.0.1", "-U", user, "-d", database, "-tAc", sql);

    public static async Task<bool> CanLoginAsync(string container, string user, string password, string database)
    {
        var result = await DockerCli.RunAsync("exec", "-e", "PGPASSWORD=" + password, container,
            "psql", "-h", "127.0.0.1", "-U", user, "-d", database, "-tAc", "SELECT 1");
        return result.ExitCode == 0 && result.Out == "1";
    }
}
