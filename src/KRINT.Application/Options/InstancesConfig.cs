namespace KRINT.Application.Options
{
    /// <summary>Root of the YAML file pointed to by KrintOptions.InstancesFile.</summary>
    public sealed class InstancesConfig
    {
        public List<ConfigInstance> Instances { get; set; } = new();
    }

    /// <summary>Declarative shape of a single instance. Matches ProvisionRequestDto plus the
    /// fields needed for password reconciliation.</summary>
    public sealed class ConfigInstance
    {
        public string Engine { get; set; } = "";
        public string Version { get; set; } = "";
        /// <summary>Identity for matching against existing rows. Must be unique within the file.</summary>
        public string DisplayName { get; set; } = "";
        public string? DefaultDatabaseName { get; set; }
        /// <summary>Custom root password. Empty = auto-generate the first time, then KRINT
        /// remembers it and never rotates it on its own.</summary>
        public string? Password { get; set; }
        public bool IsPublic { get; set; }
        public List<string> Databases { get; set; } = new();
        public List<ConfigUser> Users { get; set; } = new();
        public List<string> Plugins { get; set; } = new();
    }

    public sealed class ConfigUser
    {
        public string Name { get; set; } = "";
        public string? Password { get; set; }
        public List<string> GrantDatabases { get; set; } = new();
    }
}
