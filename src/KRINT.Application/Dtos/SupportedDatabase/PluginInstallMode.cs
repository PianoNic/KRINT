namespace KRINT.Application.Dtos.SupportedDatabase
{
    /// <summary>How a plugin is enabled at provisioning time.</summary>
    public enum PluginInstallMode
    {
        /// <summary>Runs CREATE EXTENSION on the default database after readiness. Postgres-family.</summary>
        PgExtension,
        /// <summary>Swaps the container image for a variant that bundles the module. Useful when
        /// the plugin requires native .so files not present in the base image.</summary>
        DockerImageSwap,
        /// <summary>Sets an env var on the container (e.g. NEO4J_PLUGINS=["apoc"]).</summary>
        EnvFlag,
        /// <summary>Runs an install command inside the container (e.g. elasticsearch-plugin install).</summary>
        ContainerExec,
    }
}
