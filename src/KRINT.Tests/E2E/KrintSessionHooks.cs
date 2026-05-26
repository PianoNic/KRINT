using System.Threading.Tasks;
using TUnit.Core;

namespace KRINT.Tests.E2E;

/// <summary>Session-wide hooks: boot the KRINT stack once, tear it down at the end.</summary>
public static class KrintSessionHooks
{
    [Before(HookType.TestSession)]
    public static async Task StartStack()
    {
        // Set KRINT_SKIP_E2E=1 to run unit tests without bringing up the throwaway compose
        // stack (handy on machines without Docker or when iterating on pure C# tests).
        // E2E tests themselves still call KrintStack.GetAsync() on demand, so they'll surface
        // the missing stack with their own failure.
        if (string.Equals(Environment.GetEnvironmentVariable("KRINT_SKIP_E2E"), "1", StringComparison.Ordinal))
            return;
        // Touch the lazy stack so first-test latency isn't surprising in the log.
        await KrintStack.GetAsync();
    }

    [After(HookType.TestSession)]
    public static async Task StopStack()
    {
        await KrintTestFixture.DisposeAsync();
        await KrintStack.StopSharedAsync();
    }
}
