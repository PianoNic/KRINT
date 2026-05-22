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

        public Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default)
        {
            // Mongo edits require document-level semantics (replace by _id, type-aware patches).
            // Out of scope for v1 row editing - surface a clear error to the UI.
            throw new NotSupportedException("Row editing is not supported for Mongo yet.");
        }

        public Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Row insertion is not supported for Mongo yet.");

        public Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Row deletion is not supported for Mongo yet.");

        public async Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            var db = Connect(target).GetDatabase(database);
            await db.DropCollectionAsync(table, cancellationToken);
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
