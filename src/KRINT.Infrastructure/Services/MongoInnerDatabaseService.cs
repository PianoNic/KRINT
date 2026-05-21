using KRINT.Infrastructure.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace KRINT.Infrastructure.Services
{
    public class MongoInnerDatabaseService : IInnerDatabaseService
    {
        private static readonly HashSet<string> SystemDatabases = new(StringComparer.OrdinalIgnoreCase)
        {
            "admin", "local", "config",
        };

        public string Engine => "mongo";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            var client = CreateClient(target);
            using var cursor = await client.ListDatabaseNamesAsync(cancellationToken);
            var names = await cursor.ToListAsync(cancellationToken);
            return names.Where(n => !SystemDatabases.Contains(n)).ToList();
        }

        public Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            // Mongo creates a database lazily — force it by running a no-op command against it.
            var client = CreateClient(target);
            var db = client.GetDatabase(name);
            return db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken)
                // Ping doesn't materialise the DB; create a sentinel collection then drop it.
                .ContinueWith(async _ =>
                {
                    await db.CreateCollectionAsync("__krint_init", cancellationToken: cancellationToken);
                    await db.DropCollectionAsync("__krint_init", cancellationToken);
                }, cancellationToken).Unwrap();
        }

        public async Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            if (SystemDatabases.Contains(name))
            {
                throw new InvalidOperationException($"Refusing to drop system database '{name}'.");
            }

            var client = CreateClient(target);
            await client.DropDatabaseAsync(name, cancellationToken);
        }

        private static IMongoClient CreateClient(InnerDatabaseTarget target)
        {
            var settings = MongoClientSettings.FromConnectionString(
                $"mongodb://{Uri.EscapeDataString(target.Username)}:{Uri.EscapeDataString(target.Password)}@{target.Host}:{target.Port}/?authSource=admin");
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            return new MongoClient(settings);
        }
    }
}
