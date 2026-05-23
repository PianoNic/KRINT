using System.Collections.Concurrent;
using Docker.DotNet;
using Docker.DotNet.Models;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    public sealed class ContainerExecRegistry(IDockerClient docker) : IContainerExecRegistry
    {
        private readonly ConcurrentDictionary<string, ContainerExecSession> _sessions = new();

        public async Task<IContainerExecSession> StartAsync(string containerId, uint cols, uint rows, CancellationToken cancellationToken = default)
        {
            // Try bash first, fall back to sh. We don't probe upfront — instead the exec is
            // created against a tiny shell script that picks whichever is on PATH.
            var exec = await docker.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
            {
                AttachStdin = true,
                AttachStdout = true,
                AttachStderr = true,
                Tty = true,
                Cmd = new[] { "/bin/sh", "-c", "if command -v bash >/dev/null 2>&1; then exec bash -l; else exec sh -l; fi" },
                Env = new[] { "TERM=xterm-256color" },
            }, cancellationToken);

            var stream = await docker.Exec.StartAndAttachContainerExecAsync(exec.ID, tty: true, cancellationToken);

            try
            {
                await docker.Exec.ResizeContainerExecTtyAsync(exec.ID, new ContainerResizeParameters { Height = rows, Width = cols }, cancellationToken);
            }
            catch
            {
                // Resize fails on some daemons before any output has been produced. Safe to ignore;
                // the client will retry as soon as the xterm fit-addon kicks in.
            }

            var session = new ContainerExecSession(exec.ID, stream, docker);
            _sessions[session.Id] = session;
            session.Start();
            session.Exited += async code =>
            {
                _sessions.TryRemove(session.Id, out _);
                await Task.CompletedTask;
            };
            return session;
        }

        public IContainerExecSession? Get(string sessionId) => _sessions.TryGetValue(sessionId, out var s) ? s : null;

        public async Task EndAsync(string sessionId)
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                await session.DisposeAsync();
            }
        }
    }

    internal sealed class ContainerExecSession(string id, MultiplexedStream stream, IDockerClient docker) : IContainerExecSession
    {
        public string Id { get; } = id;
        public event Func<ReadOnlyMemory<byte>, Task>? Output;
        public event Func<long?, Task>? Exited;

        private readonly CancellationTokenSource _cts = new();
        private Task? _pump;
        private int _disposed;

        public void Start()
        {
            _pump = Task.Run(PumpAsync);
        }

        private async Task PumpAsync()
        {
            var buffer = new byte[16 * 1024];
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var read = await stream.ReadOutputAsync(buffer, 0, buffer.Length, _cts.Token);
                    if (read.EOF) break;
                    if (read.Count == 0) continue;
                    var slice = new ReadOnlyMemory<byte>(buffer, 0, read.Count).ToArray();
                    if (Output is { } cb)
                    {
                        try { await cb(slice); } catch { /* subscriber gone */ }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                long? exitCode = null;
                try
                {
                    var inspect = await docker.Exec.InspectContainerExecAsync(Id, CancellationToken.None);
                    exitCode = inspect.ExitCode;
                }
                catch { /* container gone */ }
                if (Exited is { } cb)
                {
                    try { await cb(exitCode); } catch { /* subscriber gone */ }
                }
            }
        }

        public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            if (_disposed != 0) return;
            await stream.WriteAsync(data.ToArray(), 0, data.Length, cancellationToken);
        }

        public Task ResizeAsync(uint cols, uint rows, CancellationToken cancellationToken = default)
        {
            return docker.Exec.ResizeContainerExecTtyAsync(Id, new ContainerResizeParameters { Height = rows, Width = cols }, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _cts.Cancel(); } catch { }
            try { stream.Dispose(); } catch { }
            if (_pump is not null)
            {
                try { await _pump; } catch { }
            }
            _cts.Dispose();
        }
    }
}
