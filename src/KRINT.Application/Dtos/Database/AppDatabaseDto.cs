namespace KRINT.Application.Dtos.Database
{
    /// <summary>A database KRINT created for one app inside an existing instance, with the
    /// credentials scoped to it where the engine has users.</summary>
    public record AppDatabaseDto
    {
        public required string Database { get; init; }
        public required string Username { get; init; }
        /// <summary>Shown once: KRINT does not store inner user passwords. A reset issues a new one.
        /// For engines without users this is the instance password.</summary>
        public required string Password { get; init; }
        /// <summary>True when <see cref="Username"/> is a user that can only reach this database;
        /// false when the engine has no users and the instance credentials are used.</summary>
        public required bool HasScopedUser { get; init; }
        /// <summary>For containers on the same host; null when the host has no app route yet.</summary>
        public string? AppConnectionString { get; init; }
        /// <summary>From a shell on the host running the instance.</summary>
        public required string ConnectionString { get; init; }
    }

    public record CreateAppDatabaseDto
    {
        public required string Name { get; init; }
    }
}
