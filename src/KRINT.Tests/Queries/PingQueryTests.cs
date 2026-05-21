using KRINT.Application.Queries;

namespace KRINT.Tests.Queries
{
    public class PingQueryTests
    {
        [Test]
        public async Task Handle_ReturnsPong()
        {
            var handler = new PingQueryHandler();

            var result = await handler.Handle(new PingQuery(), CancellationToken.None);

            await Assert.That(result).IsEqualTo("pong");
        }
    }
}
