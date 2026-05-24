using KRINT.Application.Queries.SupportedDatabase;
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

            var keys = result.Select(d => d.Key).ToList();
            foreach (var expected in new[] { "postgres", "mysql", "mariadb", "mongo" })
            {
                await Assert.That(keys).Contains(expected);
            }
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
