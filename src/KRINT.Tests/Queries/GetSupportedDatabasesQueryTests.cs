using KRINT.Application.Queries;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Tests.Queries
{
    public class GetSupportedDatabasesQueryTests
    {
        private class FakeVersionService : IDatabaseVersionService
        {
            public Task<IReadOnlyList<string>> GetSupportedVersionsAsync(string engineKey, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<string>>(new[] { "X", "Y" });
            }
        }

        [Test]
        public async Task Handle_DefaultQuery_ReturnsCatalogWithKnownEngines()
        {
            var handler = new GetSupportedDatabasesQueryHandler(new FakeVersionService());

            var result = await handler.Handle(new GetSupportedDatabasesQuery(), CancellationToken.None);

            await Assert.That(result.Select(d => d.Key)).IsEquivalentTo(new[] { "postgres", "mysql", "mariadb", "mongo" });
        }

        [Test]
        public async Task Handle_DefaultQuery_PopulatesVersionsFromService()
        {
            var handler = new GetSupportedDatabasesQueryHandler(new FakeVersionService());

            var result = await handler.Handle(new GetSupportedDatabasesQuery(), CancellationToken.None);

            await Assert.That(result.All(d => d.Versions.Count == 2)).IsTrue();
        }
    }
}
