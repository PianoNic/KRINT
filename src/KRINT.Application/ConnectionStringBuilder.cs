namespace KRINT.Application
{
    public static class ConnectionStringBuilder
    {
        public static string Build(string engine, string host, int port, string username, string password, string database)
        {
            return engine switch
            {
                "postgres" => $"postgres://{username}:{password}@{host}:{port}/{database}",
                "mysql" => $"mysql://{username}:{password}@{host}:{port}/{database}",
                "mariadb" => $"mariadb://{username}:{password}@{host}:{port}/{database}",
                "mongo" => $"mongodb://{username}:{password}@{host}:{port}/{database}?authSource=admin",
                _ => throw new ArgumentException($"Unsupported engine '{engine}'.", nameof(engine)),
            };
        }

        public static string VaultKeyFor(string containerName) => $"db.{containerName}.password";
    }
}
