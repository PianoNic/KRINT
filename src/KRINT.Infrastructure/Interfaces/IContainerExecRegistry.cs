namespace KRINT.Infrastructure.Interfaces
{
    public interface IContainerExecRegistry
    {
        /// <summary>Creates and attaches an interactive exec session. The session does not read
        /// output until the caller has subscribed to <see cref="IContainerExecSession.Output"/> /
        /// <see cref="IContainerExecSession.Exited"/> and calls <see cref="IContainerExecSession.Start"/>,
        /// so the shell's first bytes are never lost. The registry disposes the session when it exits.</summary>
        Task<IContainerExecSession> StartAsync(string containerId, uint cols, uint rows, CancellationToken cancellationToken = default);
        IContainerExecSession? Get(string sessionId);
        Task EndAsync(string sessionId);
    }

    public interface IContainerExecSession : IAsyncDisposable
    {
        string Id { get; }
        event Func<ReadOnlyMemory<byte>, Task>? Output;
        event Func<long?, Task>? Exited;

        /// <summary>Begin pumping output. Call once, after subscribing to the events.</summary>
        void Start();

        Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
        Task ResizeAsync(uint cols, uint rows, CancellationToken cancellationToken = default);
    }
}
