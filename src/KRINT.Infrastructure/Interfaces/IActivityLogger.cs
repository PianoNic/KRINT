namespace KRINT.Infrastructure.Interfaces
{
    public interface IActivityLogger
    {
        Task LogAsync(string action, string target, Guid? instanceId = null, string? engine = null, string? details = null, CancellationToken cancellationToken = default);
    }
}
