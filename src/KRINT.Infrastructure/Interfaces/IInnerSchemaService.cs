namespace KRINT.Infrastructure.Interfaces
{
    public record TableSummary(string Name, string Kind /* "table" | "view" | "collection" */);

    public record TableRows(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows, long? TotalCount);

    public interface IInnerSchemaService
    {
        string Engine { get; }

        Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default);

        Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default);
    }
}
