namespace KRINT.Infrastructure.Interfaces
{
    /// <summary>One Qdrant point with its raw vector and payload, for the cluster view.</summary>
    public record VectorPoint(string Id, IReadOnlyList<float> Vector, string Payload);

    /// <summary>Fetches points *with* their vectors (the normal browse path omits them) so the
    /// frontend can reduce them to 2D/3D and plot clusters. Qdrant-only for now.</summary>
    public interface IQdrantVectorService
    {
        Task<IReadOnlyList<VectorPoint>> FetchAsync(InnerDatabaseTarget target, string collection, int limit, CancellationToken cancellationToken = default);
    }
}
