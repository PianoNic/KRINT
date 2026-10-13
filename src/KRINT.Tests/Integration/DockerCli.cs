using System.Diagnostics;
using System.Threading.Tasks;

namespace KRINT.Tests.Integration;

/// <summary>
/// Thin wrapper over the docker CLI for assertions the API can't make on its own (container
/// counts, engine-native clients inside an instance, restarts). The CLI is already required by
/// the suite: KrintStack drives compose through it.
/// </summary>
internal static class DockerCli
{
    public sealed record Result(int ExitCode, string StdOut, string StdErr)
    {
        public string Out => StdOut.Trim();
    }

    public static async Task<Result> RunAsync(params string[] args)
    {
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start docker CLI");
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return new Result(proc.ExitCode, await stdout, await stderr);
    }

    /// <summary>Runs docker and throws with stderr when it exits non-zero.</summary>
    public static async Task<string> RunCheckedAsync(params string[] args)
    {
        var result = await RunAsync(args);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"docker {string.Join(' ', args)} failed ({result.ExitCode}): {result.StdErr}{result.StdOut}");
        return result.Out;
    }

    public static Task<string> ExecAsync(string container, params string[] command) =>
        RunCheckedAsync(["exec", container, .. command]);

    /// <summary>Names of all containers (any state) carrying KRINT's instance-id label.</summary>
    public static async Task<string[]> ContainersForInstanceAsync(Guid instanceId)
    {
        var output = await RunCheckedAsync("ps", "-a", "--filter", $"label=krint.instance-id={instanceId}", "--format", "{{.Names}}");
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static async Task<bool> ContainerExistsAsync(string name)
    {
        var output = await RunCheckedAsync("ps", "-a", "--filter", $"name=^{name}$", "--format", "{{.Names}}");
        return output.Length > 0;
    }
}
