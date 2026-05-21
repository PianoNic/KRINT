using System.Threading.Tasks;
using TUnit.Core;

namespace KRINT.Tests.E2E;

/// <summary>Session-wide hooks: boot the KRINT stack once, tear it down at the end.</summary>
public static class KrintSessionHooks
{
    [Before(HookType.TestSession)]
    public static async Task StartStack()
    {
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
