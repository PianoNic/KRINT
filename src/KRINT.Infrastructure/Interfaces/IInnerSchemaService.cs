namespace KRINT.Infrastructure.Interfaces
{
    public record TableSummary(string Name, string Kind /* "table" | "view" | "collection" */);

    /// <summary>Per-column metadata so the UI can render type-aware inputs and know which
    /// columns to mark read-only. Engines that can't or don't populate this leave it null on
    /// TableRows; the frontend falls back to its hardcoded protected-column list in that case.</summary>
    public record ColumnInfo(string Name, string Type, bool Nullable, bool IsPrimaryKey, bool IsGenerated);

    public record TableRows(
        IReadOnlyList<string> Columns,
        IReadOnlyList<IReadOnlyList<string?>> Rows,
        long? TotalCount,
        IReadOnlyList<ColumnInfo>? ColumnInfos = null);

    /// <summary>
    /// Identifies a single row by the values it had when it was last read. Same column order as TableRows.Columns.
    /// </summary>
    public record UpdateRowRequest(IReadOnlyList<string> Columns, IReadOnlyList<string?> OriginalValues, IReadOnlyList<string?> NewValues);

    public record BulkUpdateRowsRequest(IReadOnlyList<UpdateRowRequest> Updates);

    public record InsertRowRequest(IReadOnlyList<string> Columns, IReadOnlyList<string?> Values);

    public record DeleteRowRequest(IReadOnlyList<string> Columns, IReadOnlyList<string?> OriginalValues);

    public interface IInnerSchemaService
    {
        string Engine { get; }

        Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default);

        Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default);

        Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default);

        /// <summary>Default implementation just loops UpdateRowAsync without a transaction;
        /// SQL-family services override to wrap everything in a single transaction so the user's
        /// Save is all-or-nothing.</summary>
        async Task BulkUpdateRowsAsync(InnerDatabaseTarget target, string database, string table, BulkUpdateRowsRequest request, CancellationToken cancellationToken = default)
        {
            foreach (var update in request.Updates)
                await UpdateRowAsync(target, database, table, update, cancellationToken);
        }

        Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default);

        Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default);

        Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default);
    }
}
