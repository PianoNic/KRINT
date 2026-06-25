using System.Runtime.InteropServices;
using Docker.DotNet.Models;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;
using Microsoft.AspNetCore.SignalR.Client;

namespace KRINT.API.Nodes
{
    /// <summary>Runs only when this process boots in the <c>node</c> role. It dials OUT to the control
    /// plane's <c>/hubs/node</c> over SignalR (NAT-friendly), registers itself, and answers control-plane
    /// invocations. Phase 1 only proves the channel (<c>Ping</c>); routing real Docker work over it comes
    /// later. Booting never blocks on the control plane being up - the connection retries via automatic
    /// reconnect.</summary>
    public class NodeAgentHostedService(
        IConfiguration configuration,
        IServiceProvider services,
        ILogger<NodeAgentHostedService> logger) : IHostedService, IAsyncDisposable
    {
        private HubConnection? _connection;
        private string _nodeId = "";
        // Active log follows started by the control plane, so StopLogStream can cancel them.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _logStreams = new();
        // Active interactive exec sessions keyed by the control-plane session id.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IContainerExecSession> _execSessions = new();

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var controlPlaneUrl = configuration["Node:ControlPlaneUrl"];
            var token = configuration["Node:Token"];

            if (string.IsNullOrWhiteSpace(controlPlaneUrl) || string.IsNullOrWhiteSpace(token))
            {
                logger.LogError("Node role is active but Node:ControlPlaneUrl and/or Node:Token are not set. The agent will not connect.");
                return Task.CompletedTask;
            }

            // A stable id identifies this node across reconnects so provisioned instances keep pointing
            // at it. Pin it via Node:Id; if unset we generate one but it won't survive a restart.
            if (!Guid.TryParse(configuration["Node:Id"], out var nodeId))
            {
                nodeId = Guid.NewGuid();
                logger.LogWarning("Node:Id is not set; generated {NodeId} for this run. Set Node:Id to keep a stable identity across restarts.", nodeId);
            }
            _nodeId = nodeId.ToString();

            var name = configuration["Node:Name"];
            if (string.IsNullOrWhiteSpace(name)) name = Environment.MachineName;

            var hubUrl = $"{controlPlaneUrl.TrimEnd('/')}/hubs/node?access_token={Uri.EscapeDataString(token)}";

            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            // Server -> node calls. Ping is the phase-1 channel proof.
            _connection.On("Ping", () => "pong");
            RegisterDockerHandlers(_connection);
            RegisterInnerDbHandlers(_connection);
            RegisterExecHandlers(_connection);

            // Re-register after every (re)connect, since the registry is keyed by connection id and a
            // reconnect yields a fresh one.
            _connection.Reconnected += async _ => await RegisterAsync(name, CancellationToken.None);

            // Kick off the connect loop in the background so app startup isn't blocked on the control plane.
            _ = ConnectLoopAsync(name, hubUrl);
            return Task.CompletedTask;
        }

        private async Task ConnectLoopAsync(string name, string hubUrl)
        {
            var delay = TimeSpan.FromSeconds(2);
            while (_connection is not null)
            {
                try
                {
                    await _connection.StartAsync();
                    logger.LogInformation("Node agent connected to control plane at {Url}.", hubUrl);
                    await RegisterAsync(name, CancellationToken.None);
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Node agent could not reach the control plane ({Message}); retrying in {Delay}s.", ex.Message, delay.TotalSeconds);
                    await Task.Delay(delay);
                    delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
                }
            }
        }

        private async Task RegisterAsync(string name, CancellationToken cancellationToken)
        {
            if (_connection is null) return;

            var dockerVersion = "unknown";
            try
            {
                using var scope = services.CreateScope();
                var docker = scope.ServiceProvider.GetRequiredService<IDockerService>();
                dockerVersion = await docker.GetVersionAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Node agent could not read the local Docker version: {Message}", ex.Message);
            }

            var registration = new NodeRegistrationDto(
                Id: _nodeId,
                Name: name,
                MachineName: Environment.MachineName,
                Os: RuntimeInformation.OSDescription,
                DockerVersion: dockerVersion);

            try
            {
                await _connection.InvokeAsync("Register", registration, cancellationToken);
                logger.LogInformation("Node agent registered as {Name}.", name);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Node agent failed to register: {Message}", ex.Message);
            }
        }

        // The control plane invokes these on the node's connection (RemoteDockerService); each runs
        // against the node's local Docker daemon and returns the result. Void Docker ops return a
        // bool because SignalR client-result invocations must return a value.
        private void RegisterDockerHandlers(HubConnection c)
        {
            c.On("GetVersion", () => WithDocker(d => d.GetVersionAsync()));
            c.On<bool, IList<ContainerListResponse>>("ListContainers", all => WithDocker(d => d.ListContainersAsync(all)));
            c.On<string, ContainerInspectResponse>("InspectContainer", id => WithDocker(d => d.InspectContainerAsync(id)));
            c.On<string, ContainerResourceSample?>("GetContainerResourceUsage", id => WithDocker(d => d.GetContainerResourceUsageAsync(id)));
            c.On<string, string, bool>("PullImage", (image, tag) => WithDocker(async d => { await d.PullImageAsync(image, tag); return true; }));
            c.On<CreateContainerSpec, CreateContainerResponse>("CreateContainer", spec => WithDocker(d => d.CreateContainerAsync(ToParameters(spec))));
            c.On<string, bool>("StartContainer", id => WithDocker(d => d.StartContainerAsync(id)));
            c.On<string, bool>("StopContainer", id => WithDocker(async d => { await d.StopContainerAsync(id); return true; }));
            c.On<string, bool, bool>("RemoveContainer", (id, force) => WithDocker(async d => { await d.RemoveContainerAsync(id, force); return true; }));
            c.On<string, bool, bool>("RemoveVolume", (name, force) => WithDocker(async d => { await d.RemoveVolumeAsync(name, force); return true; }));
            c.On<string, IList<string>, byte[]>("ExecCapture", (id, cmd) => WithDocker(d => d.ExecCaptureAsync(id, cmd)));
            c.On<string, IList<string>, byte[], bool>("ExecWithStdin", (id, cmd, data) => WithDocker(async d => { await d.ExecWithStdinAsync(id, cmd, new MemoryStream(data)); return true; }));

            // Log streaming: follow the container's logs locally and push each frame back; the control
            // plane relays them to the browser. Returns immediately - the follow runs in the background.
            c.On<string, string, int, bool>("StartLogStream", (streamId, containerId, tail) =>
            {
                var cts = new CancellationTokenSource();
                _logStreams[streamId] = cts;
                _ = Task.Run(() => PumpLogsAsync(streamId, containerId, tail, cts.Token));
                return Task.FromResult(true);
            });
            c.On<string, bool>("StopLogStream", streamId =>
            {
                if (_logStreams.TryRemove(streamId, out var cts)) cts.Cancel();
                return Task.FromResult(true);
            });
        }

        // Interactive console: run the exec on this node's daemon and bridge its TTY over the
        // connection. Output/Exited are pushed to the control plane keyed by the control-plane session
        // id; the control plane relays them to the browser. Input/resize/end come back the same way.
        private void RegisterExecHandlers(HubConnection c)
        {
            c.On<string, string, uint, uint, bool>("StartExec", async (sessionId, containerId, cols, rows) =>
            {
                var registry = services.GetRequiredService<IContainerExecRegistry>();
                var session = await registry.StartAsync(containerId, cols, rows);
                _execSessions[sessionId] = session;
                session.Output += async data =>
                {
                    if (_connection is null) return;
                    try { await _connection.SendAsync("ExecOutput", sessionId, Convert.ToBase64String(data.Span)); } catch { }
                };
                session.Exited += async code =>
                {
                    _execSessions.TryRemove(sessionId, out _);
                    if (_connection is null) return;
                    try { await _connection.SendAsync("ExecExited", sessionId, code); } catch { }
                };
                return true;
            });
            c.On<string, string, bool>("WriteExec", async (sessionId, base64) =>
            {
                if (_execSessions.TryGetValue(sessionId, out var session))
                    await session.WriteAsync(Convert.FromBase64String(base64));
                return true;
            });
            c.On<string, uint, uint, bool>("ResizeExec", async (sessionId, cols, rows) =>
            {
                if (_execSessions.TryGetValue(sessionId, out var session))
                    await session.ResizeAsync(cols, rows);
                return true;
            });
            c.On<string, bool>("EndExec", async (sessionId) =>
            {
                if (_execSessions.TryRemove(sessionId, out var session))
                    await services.GetRequiredService<IContainerExecRegistry>().EndAsync(session.Id);
                return true;
            });
        }

        private async Task PumpLogsAsync(string streamId, string containerId, int tail, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = services.CreateScope();
                var docker = scope.ServiceProvider.GetRequiredService<IDockerService>();
                await foreach (var frame in docker.StreamLogsAsync(containerId, tail, cancellationToken))
                {
                    if (_connection is null) break;
                    await _connection.SendAsync("LogFrame", streamId, frame, cancellationToken);
                }
            }
            catch (OperationCanceledException) { /* StopLogStream */ }
            catch (Exception ex) { logger.LogWarning("Log follow {StreamId} ended: {Message}", streamId, ex.Message); }
            finally
            {
                _logStreams.TryRemove(streamId, out _);
                if (_connection is not null)
                    try { await _connection.SendAsync("LogStreamCompleted", streamId, CancellationToken.None); } catch { }
            }
        }

        private async Task<T> WithDocker<T>(Func<IDockerService, Task<T>> work)
        {
            using var scope = services.CreateScope();
            return await work(scope.ServiceProvider.GetRequiredService<IDockerService>());
        }

        // Inner-DB operations dispatched from the control plane. Each runs the node's local engine
        // service (the target arrives with no NodeId, so the routing resolver runs it in-process)
        // against the container on this node's loopback, and returns the same DTO the UI consumes.
        private void RegisterInnerDbHandlers(HubConnection c)
        {
            c.On<InnerDatabaseTarget, IReadOnlyList<string>>("Db.List", t => Inner(s => s.GetRequiredService<IInnerDatabaseServiceResolver>().Resolve(t.Engine).ListAsync(t)));
            c.On<InnerDatabaseTarget, string, bool>("Db.Create", (t, n) => Inner(async s => { await s.GetRequiredService<IInnerDatabaseServiceResolver>().Resolve(t.Engine).CreateAsync(t, n); return true; }));
            c.On<InnerDatabaseTarget, string, bool>("Db.Drop", (t, n) => Inner(async s => { await s.GetRequiredService<IInnerDatabaseServiceResolver>().Resolve(t.Engine).DropAsync(t, n); return true; }));

            c.On<InnerDatabaseTarget, IReadOnlyList<string>>("User.List", t => Inner(s => s.GetRequiredService<IInnerUserServiceResolver>().Resolve(t.Engine).ListAsync(t)));
            c.On<InnerDatabaseTarget, string, string, bool>("User.Create", (t, n, p) => Inner(async s => { await s.GetRequiredService<IInnerUserServiceResolver>().Resolve(t.Engine).CreateAsync(t, n, p); return true; }));
            c.On<InnerDatabaseTarget, string, bool>("User.Delete", (t, n) => Inner(async s => { await s.GetRequiredService<IInnerUserServiceResolver>().Resolve(t.Engine).DeleteAsync(t, n); return true; }));
            c.On<InnerDatabaseTarget, string, string, bool>("User.ResetPassword", (t, n, p) => Inner(async s => { await s.GetRequiredService<IInnerUserServiceResolver>().Resolve(t.Engine).ResetPasswordAsync(t, n, p); return true; }));
            c.On<InnerDatabaseTarget, string, string, bool>("User.GrantAccess", (t, u, d) => Inner(async s => { await s.GetRequiredService<IInnerUserServiceResolver>().Resolve(t.Engine).GrantAccessAsync(t, u, d); return true; }));

            c.On<InnerDatabaseTarget, string, string, int, QueryResult>("Query.Run", (t, db, sql, lim) => Inner(s =>
            {
                var svc = s.GetRequiredService<IInnerQueryServiceResolver>().TryResolve(t.Engine)
                          ?? throw new NotSupportedException($"Query console not available for engine '{t.Engine}'.");
                return svc.RunAsync(t, db, sql, lim);
            }));

            c.On<InnerDatabaseTarget, string, IReadOnlyList<TableSummary>>("Schema.ListTables", (t, db) => Inner(s => s.GetRequiredService<IInnerSchemaServiceResolver>().Resolve(t.Engine).ListTablesAsync(t, db)));
            c.On<InnerDatabaseTarget, string, string, int, int, TableRows>("Schema.FetchRows", (t, db, table, lim, off) => Inner(s => s.GetRequiredService<IInnerSchemaServiceResolver>().Resolve(t.Engine).FetchRowsAsync(t, db, table, lim, off)));
            c.On<InnerDatabaseTarget, string, string, UpdateRowRequest, bool>("Schema.UpdateRow", (t, db, table, r) => Inner(async s => { await s.GetRequiredService<IInnerSchemaServiceResolver>().Resolve(t.Engine).UpdateRowAsync(t, db, table, r); return true; }));
            c.On<InnerDatabaseTarget, string, string, BulkUpdateRowsRequest, bool>("Schema.BulkUpdateRows", (t, db, table, r) => Inner(async s => { await s.GetRequiredService<IInnerSchemaServiceResolver>().Resolve(t.Engine).BulkUpdateRowsAsync(t, db, table, r); return true; }));
            c.On<InnerDatabaseTarget, string, string, InsertRowRequest, bool>("Schema.InsertRow", (t, db, table, r) => Inner(async s => { await s.GetRequiredService<IInnerSchemaServiceResolver>().Resolve(t.Engine).InsertRowAsync(t, db, table, r); return true; }));
            c.On<InnerDatabaseTarget, string, string, DeleteRowRequest, bool>("Schema.DeleteRow", (t, db, table, r) => Inner(async s => { await s.GetRequiredService<IInnerSchemaServiceResolver>().Resolve(t.Engine).DeleteRowAsync(t, db, table, r); return true; }));
            c.On<InnerDatabaseTarget, string, string, bool>("Schema.DropTable", (t, db, table) => Inner(async s => { await s.GetRequiredService<IInnerSchemaServiceResolver>().Resolve(t.Engine).DropTableAsync(t, db, table); return true; }));
        }

        private async Task<T> Inner<T>(Func<IServiceProvider, Task<T>> work)
        {
            using var scope = services.CreateScope();
            return await work(scope.ServiceProvider);
        }

        private static CreateContainerParameters ToParameters(CreateContainerSpec spec)
        {
            var portBindings = spec.PortBindings
                .GroupBy(b => b.ContainerPort)
                .ToDictionary(
                    g => g.Key,
                    g => (IList<PortBinding>)g.Select(b => new PortBinding { HostPort = b.HostPort, HostIP = b.HostIp }).ToList());

            Enum.TryParse<RestartPolicyKind>(spec.RestartPolicy, out var restartKind);

            return new CreateContainerParameters
            {
                Image = spec.Image,
                Name = spec.Name,
                Env = spec.Env,
                Cmd = spec.Cmd,
                ExposedPorts = spec.ExposedPorts.ToDictionary(p => p, _ => default(EmptyStruct)),
                HostConfig = new HostConfig
                {
                    PortBindings = portBindings,
                    Binds = spec.Binds,
                    RestartPolicy = new RestartPolicy { Name = restartKind },
                },
                Labels = spec.Labels,
            };
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_connection is not null)
                await _connection.StopAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
