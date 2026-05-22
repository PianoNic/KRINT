namespace KRINT.Infrastructure.Interfaces
{
    public record QueryResultColumn(string Name, string TypeName);

    public record QueryResult(
        IReadOnlyList<QueryResultColumn> Columns,
        IReadOnlyList<IReadOnlyList<string?>> Rows,
        int RowsAffected,
        long ElapsedMs,
        bool Truncated);

    /// <summary>
    /// Ad-hoc query runner. SQL engines implement this; document/key-value engines don't.
    /// Engines that don't register an implementation are surfaced in the UI as "console
    /// not available for this engine" rather than throwing.
    /// </summary>
    public interface IInnerQueryService
    {
        string Engine { get; }

        Task<QueryResult> RunAsync(InnerDatabaseTarget target, string database, string sql, int rowLimit, CancellationToken cancellationToken = default);
    }
}
