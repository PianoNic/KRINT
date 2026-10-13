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
    }
}
