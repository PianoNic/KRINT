using KRINT.API;
using KRINT.API.Nodes;
using KRINT.Application;

namespace KRINT.Tests.Services
{
    public class KrintExceptionHandlerTests
    {
        [Test]
        public async Task Maps_expected_exceptions_to_their_status()
        {
            await Assert.That(KrintExceptionHandler.StatusFor(new InstanceNotFoundException(Guid.NewGuid()))).IsEqualTo(404);
            await Assert.That(KrintExceptionHandler.StatusFor(new ArgumentException("bad"))).IsEqualTo(400);
            await Assert.That(KrintExceptionHandler.StatusFor(new ArgumentNullException("x"))).IsEqualTo(400);
            await Assert.That(KrintExceptionHandler.StatusFor(new NotSupportedException("no"))).IsEqualTo(400);
            await Assert.That(KrintExceptionHandler.StatusFor(new InvalidOperationException("locked"))).IsEqualTo(409);
            await Assert.That(KrintExceptionHandler.StatusFor(new NodeOfflineException(Guid.NewGuid()))).IsEqualTo(409);
        }

        [Test]
        public async Task Leaves_unexpected_exceptions_to_the_default_500()
        {
            await Assert.That(KrintExceptionHandler.StatusFor(new ObjectDisposedException("x"))).IsNull();
            await Assert.That(KrintExceptionHandler.StatusFor(new NullReferenceException())).IsNull();
        }

        [Test]
        public async Task Maps_an_unreachable_docker_daemon_to_503()
        {
            // A real failure from the Docker client, pointed at an endpoint nothing listens on.
            var endpoint = OperatingSystem.IsWindows()
                ? new Uri("npipe://./pipe/krint_test_no_docker_here")
                : new Uri("unix:///tmp/krint-test-no-docker-here.sock");
            using var client = new Docker.DotNet.DockerClientConfiguration(endpoint, defaultTimeout: TimeSpan.FromSeconds(2)).CreateClient();

            Exception? failure = null;
            try { await client.System.PingAsync(); }
            catch (Exception ex) { failure = ex; }

            await Assert.That(failure).IsNotNull();
            await Assert.That(KrintExceptionHandler.IsDockerUnreachable(failure!)).IsTrue();
            await Assert.That(KrintExceptionHandler.StatusFor(failure!)).IsEqualTo(503);
        }

        [Test]
        public async Task Keeps_http_failures_from_other_clients_unmapped()
        {
            // An engine that speaks HTTP (CouchDB, Qdrant) failing must not be reported as Docker.
            await Assert.That(KrintExceptionHandler.IsDockerUnreachable(new HttpRequestException("couchdb down"))).IsFalse();
        }
    }
}
