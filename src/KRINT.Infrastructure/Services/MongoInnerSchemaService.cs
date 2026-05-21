using System.Globalization;
using KRINT.Infrastructure.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace KRINT.Infrastructure.Services
{
    public class MongoInnerSchemaService : IInnerSchemaService
    {
        public string Engine => "mongo";

        public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            var db = Connect(target).GetDatabase(database);
            using var cursor = await db.ListCollectionNamesAsync(cancellationToken: cancellationToken);
            var names = await cursor.ToListAsync(cancellationToken);
            return names.Select(n => new TableSummary(n, "collection")).ToList();
        }

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            limit = Math.Clamp(limit, 1, 500);
            offset = Math.Max(0, offset);

            var collection = Connect(target).GetDatabase(database).GetCollection<BsonDocument>(table);
            var total = await collection.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: cancellationToken);

            var docs = await collection.Find(FilterDefinition<BsonDocument>.Empty)
                .Skip(offset)
                .Limit(limit)
                .ToListAsync(cancellationToken);

            // Render every doc as JSON; expose a single "document" column. Cheap, schema-less, faithful to mongo.
            var rows = docs
                .Select(d => (IReadOnlyList<string?>)new[] { d.ToJson(new MongoDB.Bson.IO.JsonWriterSettings { Indent = false }) })
                .ToList();

            return new TableRows(new[] { "document" }, rows, total);
        }

        private static IMongoClient Connect(InnerDatabaseTarget target)
        {
            var settings = MongoClientSettings.FromConnectionString(
                $"mongodb://{Uri.EscapeDataString(target.Username)}:{Uri.EscapeDataString(target.Password)}@{target.Host}:{target.Port}/?authSource=admin");
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            return new MongoClient(settings);
        }
    }
}
