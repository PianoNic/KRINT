using System.Net;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace KRINT.API.Nodes
{
    /// <summary>Node side of a streamed dump: an HTTP request body that runs a docker exec and writes
    /// its stdout straight into the upload as it is produced (chunked, no Content-Length), so the node
    /// never holds the whole dump in memory either. A non-zero exit fails the body mid-flight, which
    /// aborts the request; the control plane then sees an incomplete upload, never a truncated dump
    /// passed off as complete. <see cref="ExecError"/> keeps the readable reason for the RPC reply.</summary>
    internal sealed class ExecOutputContent(IDockerClient docker, string containerId, IList<string> command, TimeSpan timeout) : HttpContent
    {
        private const int StderrCap = 64 * 1024;

        /// <summary>Set when the exec itself failed (as opposed to the network).</summary>
        public Exception? ExecError { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var ct = linked.Token;

            var exec = await docker.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
            {
                Cmd = command,
                AttachStdout = true,
                AttachStderr = true,
            }, ct);

            using var output = await docker.Exec.StartAndAttachContainerExecAsync(exec.ID, tty: false, ct);
            using var stderr = new MemoryStream();
            var buffer = new byte[81920];
            try
            {
                while (true)
                {
                    var read = await output.ReadOutputAsync(buffer, 0, buffer.Length, ct);
                    if (read.EOF) break;
                    if (read.Count == 0) continue;
                    if (read.Target == MultiplexedStream.TargetStream.StandardOut)
                        await stream.WriteAsync(buffer.AsMemory(0, read.Count), ct);
                    else if (stderr.Length < StderrCap)
                        stderr.Write(buffer, 0, (int)Math.Min(read.Count, StderrCap - stderr.Length));
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                // Only the program name: the argv may carry credentials.
                ExecError = new TimeoutException($"docker exec on {containerId} exceeded {timeout.TotalMinutes:0} minutes (command: {command.FirstOrDefault()}).");
                throw ExecError;
            }

            var inspect = await docker.Exec.InspectContainerExecAsync(exec.ID, ct);
            if (inspect.ExitCode != 0)
            {
                ExecError = new InvalidOperationException($"exec exited with code {inspect.ExitCode}: {Encoding.UTF8.GetString(stderr.ToArray())}");
                throw ExecError;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
