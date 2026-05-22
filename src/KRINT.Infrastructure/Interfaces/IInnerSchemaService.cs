namespace KRINT.Infrastructure.Interfaces
{
    public record TableSummary(string Name, string Kind /* "table" | "view" | "collection" */);

    public record TableRows(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows, long? TotalCount);

    /// <summary>
    /// Identifies a single row by the values it had when it was last read. Same column order as TableRows.Columns.
    /// </summary>
    public record UpdateRowRequest(
        IReadOnlyList<string> Columns,
        IReadOnlyList<string?> OriginalValues,
        IReadOnlyList<string?> NewValues);

    public record InsertRowRequest(
        IReadOnlyList<string> Columns,
        IReadOnlyList<string?> Values);

    public record DeleteRowRequest(
        IReadOnlyList<string> Columns,
        IReadOnlyList<string?> OriginalValues);

    public interface IInnerSchemaService
    {
        string Engine { get; }

        Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default);

        Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default);

        Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default);

        Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default);

        Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default);

        Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default);
    }
}
