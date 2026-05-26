using System.Text;
using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries.Database
{
    public record ExportInstanceYamlResult(string Yaml);

    public record ExportInstanceYamlQuery(Guid Id) : IQuery<ExportInstanceYamlResult?>;

    /// <summary>
    /// Returns a YAML snippet that, pasted into instances.yaml, reproduces this instance. Use
    /// case: the user provisioned by clicking around and now wants to commit the config to a
    /// file. Externals and adopted-Docker rows export the same shape - they just can't be
    /// "provisioned from scratch" by the reconcile loader (which is fine, the user knows).
    /// <para>
    /// Inner user passwords aren't stored in the vault, so they get a placeholder comment.
    /// Replace it manually or let the next reconcile auto-generate one.
    /// </para>
    /// </summary>
    public class ExportInstanceYamlQueryHandler(
        KrintDbContext db,
        ISecretsVaultService vault,
        IInnerDatabaseServiceResolver innerDbs,
        IInnerUserServiceResolver innerUsers)
        : IQueryHandler<ExportInstanceYamlQuery, ExportInstanceYamlResult?>
    {
        public async ValueTask<ExportInstanceYamlResult?> Handle(ExportInstanceYamlQuery query, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == query.Id, cancellationToken);
            if (instance is null) return null;

            var password = await vault.RetrieveAsync(ConnectionStringBuilder.VaultKeyFor(instance), cancellationToken);

            // Best-effort enumeration. If the engine isn't reachable (stopped or remote
            // unreachable) we just emit empty lists - the snippet stays valid YAML.
            IReadOnlyList<string> innerDbList = Array.Empty<string>();
            IReadOnlyList<string> innerUserList = Array.Empty<string>();
            try
            {
                var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, instance.Id, cancellationToken);
                try { innerDbList = await innerDbs.Resolve(target.Engine).ListAsync(target, cancellationToken); } catch { }
                try { innerUserList = await innerUsers.Resolve(target.Engine).ListAsync(target, cancellationToken); } catch { }
            }
            catch
            {
                // Vault miss or unsupported engine - keep going with empty inner lists.
            }

            var sb = new StringBuilder();
            sb.Append("- engine: ").Append(instance.Engine).Append('\n');
            sb.Append("  version: ").Append(QuoteIfNeeded(instance.Version)).Append('\n');
            sb.Append("  display_name: ").Append(QuoteIfNeeded(instance.DisplayName)).Append('\n');
            sb.Append("  default_database_name: ").Append(QuoteIfNeeded(instance.DatabaseName)).Append('\n');
            if (!string.IsNullOrEmpty(password))
            {
                sb.Append("  password: ").Append(QuoteIfNeeded(password)).Append('\n');
            }
            sb.Append("  is_public: ").Append(instance.IsPublic ? "true" : "false").Append('\n');

            // Filter out the default DB so it's not also listed under databases (it's already
            // covered by default_database_name).
            var extraDbs = innerDbList
                .Where(d => !string.Equals(d, instance.DatabaseName, StringComparison.OrdinalIgnoreCase))
                .Where(d => !IsSystemDb(instance.Engine, d))
                .ToList();
            if (extraDbs.Count > 0)
            {
                sb.Append("  databases:\n");
                foreach (var name in extraDbs)
                    sb.Append("    - ").Append(QuoteIfNeeded(name)).Append('\n');
            }

            // Exclude the engine-default root account; the user re-enters it via password above.
            var extraUsers = innerUserList
                .Where(u => !string.Equals(u, instance.Username, StringComparison.OrdinalIgnoreCase))
                .Where(u => !IsSystemUser(instance.Engine, u))
                .ToList();
            if (extraUsers.Count > 0)
            {
                sb.Append("  users:\n");
                foreach (var name in extraUsers)
                {
                    sb.Append("    - name: ").Append(QuoteIfNeeded(name)).Append('\n');
                    sb.Append("      # password: <not exported - set explicitly or let KRINT auto-generate>\n");
                    sb.Append("      grant_databases: []\n");
                }
            }

            return new ExportInstanceYamlResult(sb.ToString());
        }

        private static string QuoteIfNeeded(string value)
        {
            // Always quote so the output is unambiguous and version numbers like "18.4" don't get
            // parsed as floats.
            return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
        }

        private static bool IsSystemDb(string engine, string name) => engine switch
        {
            "postgres" or "pgvector" or "timescaledb" or "cockroachdb" => name is "template0" or "template1",
            _ => false,
        };

        private static bool IsSystemUser(string engine, string name) => engine switch
        {
            "postgres" or "pgvector" or "timescaledb" => name is "pg_signal_backend" or "pg_read_all_data" or "pg_write_all_data",
            _ => false,
        };
    }
}
