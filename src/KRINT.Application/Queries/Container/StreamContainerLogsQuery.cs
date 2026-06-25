using System.Runtime.CompilerServices;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Infrastructure;
using KRINT.Application;

namespace KRINT.Application.Queries.Container
{
    public record StreamContainerLogsQuery(Guid InstanceId, int TailLines = 200) : IStreamQuery<string>;

    public class StreamContainerLogsQueryHandler(KrintDbContext db, IDockerClient docker)
        : IStreamQueryHandler<StreamContainerLogsQuery, string>
    {
        public async IAsyncEnumerable<string> Handle(StreamContainerLogsQuery query, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == query.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(query.InstanceId);
            NodeFeatureGuard.EnsureLocal(instance, "Container log streaming");

            var containerId = instance.ContainerId
                ?? throw new InvalidOperationException("Container logs are not available for externally-registered databases.");

            var parameters = new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = true,
                Tail = query.TailLines.ToString(),
                Timestamps = false,
            };

            using var stream = await docker.Containers.GetContainerLogsAsync(
                containerId, tty: false, parameters, cancellationToken);

            var buffer = new byte[16 * 1024];
            while (!cancellationToken.IsCancellationRequested)
            {
                MultiplexedStream.ReadResult read;
                try
                {
                    read = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken);
                }
                catch (OperationCanceledException) { yield break; }
                catch (IOException) { yield break; }

                if (read.EOF) yield break;
                if (read.Count == 0) continue;

                yield return Encoding.UTF8.GetString(buffer, 0, read.Count);
            }
        }
    }
}
