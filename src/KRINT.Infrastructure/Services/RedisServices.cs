using System.Globalization;
using KRINT.Infrastructure.Interfaces;
using StackExchange.Redis;

namespace KRINT.Infrastructure.Services
{
    // Redis lives outside the "databases / users / tables / rows" SQL model. The shape we expose:
    //   - "databases" = the 16 numbered logical DBs (0..15). Can't create/drop new ones.
    //   - "tables"    = one virtual table per DB named "keys" - this is the only way to fit Redis
    //                   into the schema-service contract without inventing a second contract.
    //   - "rows"      = each key in the DB, with columns [key, type, ttl_seconds, size, value].
    //   - users       = not supported in v1 (ACL is its own thing).
    //   - backup      = BGSAVE then read /data/dump.rdb out of the container.
    //
    // EngineCapabilities (declared in GetSupportedDatabasesQuery) tells the UI which buttons to
    // hide; nothing here promises capabilities the service doesn't deliver.

    internal static class RedisConnection
    {
        public static Task<IConnectionMultiplexer> ConnectAsync(InnerDatabaseTarget target, int dbNumber = -1)
        {
            var options = new ConfigurationOptions
            {
                EndPoints = { { target.Host, target.Port } },
                ConnectTimeout = 5000,
                AbortOnConnectFail = false,
                AllowAdmin = true,
            };
            if (!string.IsNullOrEmpty(target.Password)) options.Password = target.Password;
            if (!string.IsNullOrEmpty(target.Username) && target.Username != "default") options.User = target.Username;
            if (dbNumber >= 0) options.DefaultDatabase = dbNumber;
            return ConnectionMultiplexer.ConnectAsync(options).ContinueWith(t => (IConnectionMultiplexer)t.Result);
        }

        public static int ParseDatabaseNumber(string database)
        {
            if (!int.TryParse(database, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 0 || n > 15)
                throw new ArgumentException($"Redis database number must be 0..15, got '{database}'.", nameof(database));
            return n;
        }
    }

    public class RedisInnerDatabaseService : IInnerDatabaseService
    {
        public virtual string Engine => "redis";

        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            // 16 numbered DBs by convention. Exposing them as strings keeps the contract.
            IReadOnlyList<string> names = Enumerable.Range(0, 16).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
            return Task.FromResult(names);
        }

        public Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Redis databases are fixed (0..15) - they can't be created.");
        }

        public async Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            // FLUSHDB on the given DB number - wipes keys but the DB number itself remains.
            var n = RedisConnection.ParseDatabaseNumber(name);
            await using var conn = (ConnectionMultiplexer)await RedisConnection.ConnectAsync(target);
            var server = conn.GetServers().First();
            await server.FlushDatabaseAsync(n);
        }
    }

    public class RedisInnerUserService : IInnerUserService
    {
        public virtual string Engine => "redis";

        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Redis ACL user management is not exposed in this version.");

        public Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Redis ACL user management is not exposed in this version.");

        public Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Redis ACL user management is not exposed in this version.");

        public Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Redis ACL user management is not exposed in this version.");
    }

    public class RedisInnerSchemaService : IInnerSchemaService
    {
        public virtual string Engine => "redis";

        private const string KeysTable = "keys";

        public Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            // Single virtual "table" per DB - there's no real schema concept in Redis.
            RedisConnection.ParseDatabaseNumber(database);
            IReadOnlyList<TableSummary> result = new[] { new TableSummary(KeysTable, "keyspace") };
            return Task.FromResult(result);
        }

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(table, KeysTable, StringComparison.Ordinal))
                throw new ArgumentException($"Redis exposes only the virtual table 'keys', got '{table}'.");
            var dbNum = RedisConnection.ParseDatabaseNumber(database);
            limit = Math.Clamp(limit, 1, 500);
            offset = Math.Max(0, offset);

            await using var conn = (ConnectionMultiplexer)await RedisConnection.ConnectAsync(target, dbNum);
            var server = conn.GetServers().First();
            var db = conn.GetDatabase(dbNum);

            long total = await server.DatabaseSizeAsync(dbNum);

            // SCAN is the only correct way to walk keys; KEYS * blocks. Pull more than we need and slice.
            var keys = new List<RedisKey>();
            await foreach (var key in server.KeysAsync(dbNum, pageSize: 250, cursor: 0).WithCancellation(cancellationToken))
            {
                keys.Add(key);
                if (keys.Count >= offset + limit + 1) break;
            }
            var window = keys.Skip(offset).Take(limit).ToList();

            var rows = new List<IReadOnlyList<string?>>(window.Count);
            foreach (var key in window)
            {
                var type = (await db.KeyTypeAsync(key)).ToString().ToLowerInvariant();
                var ttl = await db.KeyTimeToLiveAsync(key);
                var ttlStr = ttl is null ? null : ((long)ttl.Value.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                var preview = await PreviewValueAsync(db, key, type);
                rows.Add(new[] { (string?)key.ToString(), type, ttlStr, preview });
            }

            return new TableRows(
                new[] { "key", "type", "ttl_seconds", "value" },
                rows,
                total);
        }

        private static async Task<string?> PreviewValueAsync(IDatabase db, RedisKey key, string type)
        {
            return type switch
            {
                "string" => (string?)await db.StringGetAsync(key),
                "list"   => $"[{string.Join(", ", (await db.ListRangeAsync(key, 0, 9)).Select(v => (string?)v))}]",
                "set"    => $"{{{string.Join(", ", (await db.SetMembersAsync(key)).Take(10).Select(v => (string?)v))}}}",
                "hash"   => $"{{{string.Join(", ", (await db.HashGetAllAsync(key)).Take(10).Select(e => $"{e.Name}: {e.Value}"))}}}",
                "zset"   => $"[{string.Join(", ", (await db.SortedSetRangeByRankWithScoresAsync(key, 0, 9)).Select(e => $"{e.Element}@{e.Score}"))}]",
                "stream" => "<stream>",
                _        => "<unknown>",
            };
        }

        public async Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default)
        {
            var (key, newValue) = ParseKeyValue(request.Columns, request.NewValues);
            var origKey = ColumnValue(request.Columns, request.OriginalValues, "key");
            if (!string.Equals(key, origKey, StringComparison.Ordinal))
                throw new ArgumentException("Renaming a Redis key is not supported via row edit. Delete and re-insert instead.");
            var dbNum = RedisConnection.ParseDatabaseNumber(database);
            await using var conn = (ConnectionMultiplexer)await RedisConnection.ConnectAsync(target, dbNum);
            await conn.GetDatabase(dbNum).StringSetAsync(key, newValue);
        }

        public async Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default)
        {
            var (key, value) = ParseKeyValue(request.Columns, request.Values);
            var dbNum = RedisConnection.ParseDatabaseNumber(database);
            await using var conn = (ConnectionMultiplexer)await RedisConnection.ConnectAsync(target, dbNum);
            var db = conn.GetDatabase(dbNum);
            if (await db.KeyExistsAsync(key))
                throw new InvalidOperationException($"Key '{key}' already exists. Edit it instead, or delete it first.");
            await db.StringSetAsync(key, value);
        }

        public async Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
        {
            var key = ColumnValue(request.Columns, request.OriginalValues, "key")
                ?? throw new ArgumentException("Missing 'key' column in delete request.");
            var dbNum = RedisConnection.ParseDatabaseNumber(database);
            await using var conn = (ConnectionMultiplexer)await RedisConnection.ConnectAsync(target, dbNum);
            var deleted = await conn.GetDatabase(dbNum).KeyDeleteAsync(key);
            if (!deleted) throw new InvalidOperationException($"Key '{key}' not found.");
        }

        public Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Redis has no tables. To wipe a DB use the drop-database action.");

        private static (string Key, string? Value) ParseKeyValue(IReadOnlyList<string> columns, IReadOnlyList<string?> values)
        {
            var key = ColumnValue(columns, values, "key") ?? throw new ArgumentException("Missing 'key' column.");
            var value = ColumnValue(columns, values, "value");
            return (key, value);
        }

        private static string? ColumnValue(IReadOnlyList<string> columns, IReadOnlyList<string?> values, string name)
        {
            for (var i = 0; i < columns.Count; i++)
                if (string.Equals(columns[i], name, StringComparison.OrdinalIgnoreCase)) return values[i];
            return null;
        }
    }

    public class RedisBackupService(IDockerServiceResolver dockerResolver) : IBackupService
    {
        public virtual string Engine => "redis";

        public async Task<BackupOutput> DumpAsync(BackupTarget target, CancellationToken cancellationToken = default)
        {
            // Trigger a background save (sync if BGSAVE not allowed), wait for it, then cat the rdb.
            // `redis-cli save` runs synchronously and only returns once the dump is on disk.
            var cmd = new List<string>
            {
                "sh", "-c",
                $"redis-cli {AuthFlag(target.Password)} save >/dev/null && cat /data/dump.rdb",
            };
            var bytes = await dockerResolver.Resolve(target.NodeId).ExecCaptureAsync(target.ContainerId, cmd, cancellationToken);
            return new BackupOutput(bytes, "rdb");
        }

        public async Task RestoreAsync(BackupTarget target, Stream dump, CancellationToken cancellationToken = default)
        {
            // Replace /data/dump.rdb and force reload via DEBUG RELOAD. The image already runs with
            // RDB persistence enabled, so on next start it would pick up dump.rdb too.
            var cmd = new List<string>
            {
                "sh", "-c",
                $"cat > /data/dump.rdb && redis-cli {AuthFlag(target.Password)} DEBUG RELOAD >/dev/null",
            };
            await dockerResolver.Resolve(target.NodeId).ExecWithStdinAsync(target.ContainerId, cmd, dump, cancellationToken);
        }

        private static string AuthFlag(string password)
            => string.IsNullOrEmpty(password) ? string.Empty : $"-a '{password.Replace("'", "'\\''")}' --no-auth-warning";
    }
}
