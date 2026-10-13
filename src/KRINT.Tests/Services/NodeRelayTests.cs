using KRINT.API.Nodes;

namespace KRINT.Tests.Services
{
    public class NodeRelayTests
    {
        [Test]
        public async Task Log_relay_drops_oldest_frames_when_the_reader_falls_behind()
        {
            var relay = new NodeStreamRelay();
            var reader = relay.OpenLog("s", Guid.NewGuid());

            for (var i = 0; i < 1500; i++)
                relay.PushLog("s", i.ToString());

            await Assert.That(reader.Count).IsEqualTo(1000);
            await Assert.That(reader.TryRead(out var first)).IsTrue();
            await Assert.That(first).IsEqualTo("500");
        }

        [Test]
        public async Task Dropping_a_node_fails_its_log_streams_and_returns_its_exec_sessions()
        {
            var relay = new NodeStreamRelay();
            var gone = Guid.NewGuid();
            var other = Guid.NewGuid();
            var goneReader = relay.OpenLog("gone", gone);
            var otherReader = relay.OpenLog("other", other);
            relay.RegisterExec("exec-gone", gone, "browser-1");
            relay.RegisterExec("exec-other", other, "browser-2");

            var dropped = relay.DropNode(gone);

            await Assert.That(dropped).HasCount().EqualTo(1);
            await Assert.That(dropped[0].SessionId).IsEqualTo("exec-gone");
            await Assert.That(dropped[0].BrowserConnectionId).IsEqualTo("browser-1");
            await Assert.That(goneReader.Completion.IsFaulted).IsTrue();
            await Assert.That(otherReader.Completion.IsCompleted).IsFalse();
            await Assert.That(relay.TryGetExec("exec-gone", out _, out _)).IsFalse();
            await Assert.That(relay.TryGetExec("exec-other", out _, out _)).IsTrue();
        }

        [Test]
        public async Task Disconnecting_a_node_aborts_its_connection_and_takes_it_offline()
        {
            var registry = new NodeRegistry();
            var nodeId = Guid.NewGuid();
            var aborted = false;
            registry.Register(nodeId, "conn", NodeProtocol.Current, () => aborted = true);

            registry.Disconnect(nodeId);

            await Assert.That(aborted).IsTrue();
            await Assert.That(registry.TryGetConnectionId(nodeId, out _)).IsFalse();
            await Assert.That(registry.GetProtocolVersion(nodeId)).IsEqualTo(0);
        }

        [Test]
        public async Task Heartbeat_keeps_the_reported_protocol_version()
        {
            var registry = new NodeRegistry();
            var nodeId = Guid.NewGuid();
            registry.Register(nodeId, "conn", NodeProtocol.Current);

            registry.Touch("conn");

            await Assert.That(registry.GetProtocolVersion(nodeId)).IsEqualTo(NodeProtocol.Current);
        }

        [Test]
        public async Task A_transfer_can_only_be_claimed_once()
        {
            var transfers = new NodeTransfers();
            var (id, _) = transfers.Open(Guid.NewGuid(), NodeTransferDirection.Upload, new MemoryStream(), CancellationToken.None);

            await Assert.That(transfers.TryPeek(id, out _)).IsTrue();
            await Assert.That(transfers.TryClaim(id, out _)).IsTrue();
            await Assert.That(transfers.TryClaim(id, out _)).IsFalse();
        }

        [Test]
        public async Task Abandoning_an_unclaimed_transfer_fails_it()
        {
            var transfers = new NodeTransfers();
            var (id, transfer) = transfers.Open(Guid.NewGuid(), NodeTransferDirection.Download, new MemoryStream(), CancellationToken.None);

            transfers.Abandon(id);

            await Assert.That(transfer.Completion.IsFaulted || transfer.Completion.IsCanceled).IsTrue();
            await Assert.That(transfers.TryPeek(id, out _)).IsFalse();
        }
    }
}
