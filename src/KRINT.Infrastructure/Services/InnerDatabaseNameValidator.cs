using System.Text.RegularExpressions;

namespace KRINT.Infrastructure.Services
{
    public static class InnerDatabaseNameValidator
    {
        // Conservative cross-engine identifier rule: letter/underscore, then [A-Za-z0-9_-], 1-63 chars.
        // Identifiers can't be parameterised in DDL, so we validate strictly before interpolating.
        private static readonly Regex Pattern = new("^[A-Za-z_][A-Za-z0-9_-]{0,62}$", RegexOptions.Compiled);

        // Cassandra keyspaces: a letter, then up to 47 alphanumerics/underscores (no hyphen).
        private static readonly Regex CassandraKeyspace = new("^[A-Za-z][A-Za-z0-9_]{0,47}$", RegexOptions.Compiled);

        // CouchDB databases must start with a lowercase letter and contain no uppercase letters.
        private static readonly Regex CouchDbDatabase = new("^[a-z][a-z0-9_-]{0,62}$", RegexOptions.Compiled);

        // MySQL caps user names at 32 characters (MariaDB allows 80, so it is not included here).
        private const int MySqlMaxUserLength = 32;

        public static void Require(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || !Pattern.IsMatch(name))
            {
                throw new ArgumentException($"Invalid database name '{name}'. Must start with a letter/underscore and contain only [A-Za-z0-9_-], max 63 chars.", nameof(name));
            }
        }

        /// <summary>The cross-engine rule plus whatever the engine itself is stricter about.
        /// Called before provisioning starts so a bad name never leaves a half-built instance.</summary>
        public static void RequireDatabaseForEngine(string engine, string name)
        {
            Require(name);
            switch (engine?.Trim().ToLowerInvariant())
            {
                case "cassandra" when !CassandraKeyspace.IsMatch(name):
                    throw new ArgumentException($"Invalid Cassandra keyspace '{name}'. Must start with a letter and contain only [A-Za-z0-9_], max 48 chars.", nameof(name));
                case "couchdb" when !CouchDbDatabase.IsMatch(name):
                    throw new ArgumentException($"Invalid CouchDB database '{name}'. Must start with a lowercase letter and contain only [a-z0-9_-], max 63 chars.", nameof(name));
            }
        }

        public static void RequireUserForEngine(string engine, string name)
        {
            Require(name);
            if (string.Equals(engine?.Trim(), "mysql", StringComparison.OrdinalIgnoreCase) && name.Length > MySqlMaxUserLength)
                throw new ArgumentException($"Invalid MySQL user name '{name}'. Max {MySqlMaxUserLength} chars.", nameof(name));
        }
    }
}
