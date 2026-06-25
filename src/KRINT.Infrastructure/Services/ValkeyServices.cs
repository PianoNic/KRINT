using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // Valkey is a Redis fork - wire-compatible, same `redis-server` CLI flags, same redis-cli,
    // same key/value/data-type model. We reuse the Redis services verbatim and only relabel
    // Engine for the resolver lookup.
    public sealed class ValkeyInnerDatabaseService : RedisInnerDatabaseService { public override string Engine => "valkey"; }
    public sealed class ValkeyInnerUserService     : RedisInnerUserService     { public override string Engine => "valkey"; }
    public sealed class ValkeyInnerSchemaService   : RedisInnerSchemaService   { public override string Engine => "valkey"; }
    public sealed class ValkeyBackupService(IDockerServiceResolver dockerResolver) : RedisBackupService(dockerResolver) { public override string Engine => "valkey"; }
}
