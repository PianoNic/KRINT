using KRINT.API.Nodes;
using Microsoft.AspNetCore.SignalR.Client;

namespace KRINT.Tests.Services
{
    public class NodeReconnectPolicyTests
    {
        private static TimeSpan? Delay(long attempt) =>
            new NodeReconnectPolicy().NextRetryDelay(new RetryContext { PreviousRetryCount = attempt, ElapsedTime = TimeSpan.FromMinutes(attempt) });

        [Test]
        public async Task Retries_immediately_then_backs_off()
        {
            await Assert.That(Delay(0)).IsEqualTo(TimeSpan.Zero);
            await Assert.That(Delay(1)).IsEqualTo(TimeSpan.FromSeconds(2));
        }

        [Test]
        public async Task Never_gives_up_after_a_long_outage()
        {
            await Assert.That(Delay(4)).IsEqualTo(TimeSpan.FromSeconds(30));
            await Assert.That(Delay(500)).IsEqualTo(TimeSpan.FromSeconds(30));
        }

        [Test]
        public async Task Repeated_closes_back_off_instead_of_spinning()
        {
            // The agent passes 1 for the first close, so even a token rejected right after the
            // handshake waits before dialling again.
            await Assert.That(NodeReconnectPolicy.DelayFor(1)).IsEqualTo(TimeSpan.FromSeconds(2));
            await Assert.That(NodeReconnectPolicy.DelayFor(10)).IsEqualTo(TimeSpan.FromSeconds(30));
        }
    }
}
