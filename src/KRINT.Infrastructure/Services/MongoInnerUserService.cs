using KRINT.Infrastructure.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace KRINT.Infrastructure.Services
{
    public class MongoInnerUserService : IInnerUserService
    {
        public string Engine => "mongo";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            var admin = Admin(target);
            var result = await admin.RunCommandAsync<BsonDocument>(new BsonDocument("usersInfo", 1), cancellationToken: cancellationToken);
            var users = result["users"].AsBsonArray;
            return users.Select(u => u.AsBsonDocument["user"].AsString).ToList();
        }

        public async Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            var admin = Admin(target);
            var cmd = new BsonDocument
            {
                { "createUser", name },
                { "pwd", password },
                { "roles", new BsonArray() },
            };
            await admin.RunCommandAsync<BsonDocument>(cmd, cancellationToken: cancellationToken);
        }

        public async Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            if (string.Equals(name, target.Username, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing to drop the instance's root user '{name}'.");
            }
            var admin = Admin(target);
            await admin.RunCommandAsync<BsonDocument>(new BsonDocument("dropUser", name), cancellationToken: cancellationToken);
        }

        public async Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            var admin = Admin(target);
            var cmd = new BsonDocument
            {
                { "updateUser", name },
                { "pwd", newPassword },
            };
            await admin.RunCommandAsync<BsonDocument>(cmd, cancellationToken: cancellationToken);
        }

        public async Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(user);
            InnerDatabaseNameValidator.Require(database);
            var admin = Admin(target);
            var cmd = new BsonDocument
            {
                { "grantRolesToUser", user },
                { "roles", new BsonArray { new BsonDocument { { "role", "readWrite" }, { "db", database } } } },
            };
            await admin.RunCommandAsync<BsonDocument>(cmd, cancellationToken: cancellationToken);
        }

        private static IMongoDatabase Admin(InnerDatabaseTarget target)
        {
            var settings = MongoClientSettings.FromConnectionString($"mongodb://{Uri.EscapeDataString(target.Username)}:{Uri.EscapeDataString(target.Password)}@{target.Host}:{target.Port}/?authSource=admin");
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            return new MongoClient(settings).GetDatabase("admin");
        }
    }
}
