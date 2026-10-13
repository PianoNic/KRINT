using System.Threading.Tasks;
using TUnit.Core;

namespace KRINT.Tests.Integration;

/// <summary>Session-wide hooks: boot the KRINT stack once, tear it down at the end.</summary>
public static class KrintSessionHooks
{
    [Before(HookType.TestSession)]
    public static async Task StartStack()
    {
        // KRINT_SKIP_INTEGRATION=1 (or the older KRINT_SKIP_E2E=1) runs only the unit tests,
        // handy without Docker or when iterating on pure C# code. Integration tests skip then.
        if (KrintStack.Skipped) return;
        // Touch the lazy stack so first-test latency isn't surprising in the log.
        await KrintStack.GetAsync();
    }

    [After(HookType.TestSession)]
    public static async Task StopStack()
    {
        await KrintStack.StopSharedAsync();
    }
}
