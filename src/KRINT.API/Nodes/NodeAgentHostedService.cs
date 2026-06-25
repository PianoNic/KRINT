using System.Runtime.InteropServices;
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

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var controlPlaneUrl = configuration["Node:ControlPlaneUrl"];
            var token = configuration["Node:Token"];

            if (string.IsNullOrWhiteSpace(controlPlaneUrl) || string.IsNullOrWhiteSpace(token))
            {
                logger.LogError("Node role is active but Node:ControlPlaneUrl and/or Node:Token are not set. The agent will not connect.");
                return Task.CompletedTask;
            }

            var name = configuration["Node:Name"];
            if (string.IsNullOrWhiteSpace(name)) name = Environment.MachineName;

            var hubUrl = $"{controlPlaneUrl.TrimEnd('/')}/hubs/node?access_token={Uri.EscapeDataString(token)}";

            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            // Server -> node calls. Ping is the phase-1 channel proof.
            _connection.On("Ping", () => "pong");

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
