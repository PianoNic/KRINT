using Docker.DotNet.Models;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application
{
    /// <summary>Keeps an instance's stored container id pointing at its live container. An adopted
    /// compose container that <c>docker compose up</c> recreated keeps its name but gets a new id, so
    /// the stored id goes stale and every operation on it fails with "no such container". Instance
    /// operations call this before touching the container: when the id no longer resolves, the
    /// container is looked up by its name and the new id is persisted.</summary>
    public static class ContainerIdResolver
    {
        /// <summary>Returns the inspect of the instance's live container (refreshing
        /// <see cref="Domain.DatabaseInstance.ContainerId"/> and saving when it changed), or null when
        /// the container can't be found by id or name, or the daemon/node is unreachable - the caller's
        /// own operation then reports the real problem.</summary>
        public static async Task<ContainerInspectResponse?> RefreshAsync(
            KrintDbContext db,
            Domain.DatabaseInstance instance,
            IDockerService docker,
            CancellationToken cancellationToken)
        {
            if (instance.ContainerId is null) return null;

            try
            {
                return await docker.InspectContainerAsync(instance.ContainerId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                // Stale id (or unreachable daemon) - try the name below.
            }

            if (string.IsNullOrWhiteSpace(instance.ContainerName)) return null;

            ContainerInspectResponse byName;
            try
            {
                byName = await docker.InspectContainerAsync(instance.ContainerName, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                return null;
            }

            // Docker also matches id prefixes; only accept an exact name match.
            if (byName?.ID is null || !string.Equals(byName.Name?.TrimStart('/'), instance.ContainerName, StringComparison.Ordinal))
                return null;

            if (byName.ID != instance.ContainerId)
            {
                instance.ContainerId = byName.ID;
                await db.SaveChangesAsync(cancellationToken);
            }
            return byName;
        }
    }
}
