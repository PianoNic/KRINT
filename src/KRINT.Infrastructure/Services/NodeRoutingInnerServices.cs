using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // Each wrapper decorates the local engine service. When the target carries a NodeId the call is
    // dispatched to that node over SignalR (it runs the same engine service against its own loopback
    // and returns the DTO); otherwise it runs locally as before. The NodeId is stripped from the wire
    // payload so the node executes locally instead of re-routing. Void engine ops map to a bool result
    // because SignalR client-result invocations must return a value.

    internal static class NodeRouting
    {
        public static InnerDatabaseTarget Wire(InnerDatabaseTarget t) => t with { NodeId = null };
    }

    internal sealed class RoutingInnerDatabaseService(IInnerDatabaseService local, INodeRpc rpc) : IInnerDatabaseService
    {
        public string Engine => local.Engine;

        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget t, CancellationToken ct = default)
            => t.NodeId is { } n ? rpc.InvokeAsync<IReadOnlyList<string>>(n, "Db.List", [NodeRouting.Wire(t)], ct) : local.ListAsync(t, ct);

        public Task CreateAsync(InnerDatabaseTarget t, string name, CancellationToken ct = default)
            => t.NodeId is { } n ? rpc.InvokeAsync<bool>(n, "Db.Create", [NodeRouting.Wire(t), name], ct) : local.CreateAsync(t, name, ct);

        public Task DropAsync(InnerDatabaseTarget t, string name, CancellationToken ct = default)
            => t.NodeId is { } n ? rpc.InvokeAsync<bool>(n, "Db.Drop", [NodeRouting.Wire(t), name], ct) : local.DropAsync(t, name, ct);
    }

    internal sealed class RoutingInnerUserService(IInnerUserService local, INodeRpc rpc) : IInnerUserService
    {
        public string Engine => local.Engine;

        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget t, CancellationToken ct = default)
            => t.NodeId is { } n ? rpc.InvokeAsync<IReadOnlyList<string>>(n, "User.List", [NodeRouting.Wire(t)], ct) : local.ListAsync(t, ct);

        public Task CreateAsync(InnerDatabaseTarget t, string name, string password, CancellationToken ct = default)
            => t.NodeId is { } n ? rpc.InvokeAsync<bool>(n, "User.Create", [NodeRouting.Wire(t), name, password], ct) : local.CreateAsync(t, name, password, ct);

        public Task DeleteAsync(InnerDatabaseTarget t, string name, CancellationToken ct = default)
            => t.NodeId is { } n ? rpc.InvokeAsync<bool>(n, "User.Delete", [NodeRouting.Wire(t), name], ct) : local.DeleteAsync(t, name, ct);

        public Task ResetPasswordAsync(InnerDatabaseTarget t, string name, string newPassword, CancellationToken ct = default)
            => t.NodeId is { } n ? rpc.InvokeAsync<bool>(n, "User.ResetPassword", [NodeRouting.Wire(t), name, newPassword], ct) : local.ResetPasswordAsync(t, name, newPassword, ct);

        public Task GrantAccessAsync(InnerDatabaseTarget t, string user, string database, CancellationToken ct = default)
            => t.NodeId is { } n ? rpc.InvokeAsync<bool>(n, "User.GrantAccess", [NodeRouting.Wire(t), user, database], ct) : local.GrantAccessAsync(t, user, database, ct);
    }

    internal sealed class RoutingInnerQueryService(IInnerQueryService local, INodeRpc rpc) : IInnerQueryService
    {
        public string Engine => local.Engine;

        public Task<QueryResult> RunAsync(InnerDatabaseTarget t, string database, string sql, int rowLimit, CancellationToken ct = default)
            => t.NodeId is { } n ? rpc.InvokeAsync<QueryResult>(n, "Query.Run", [NodeRouting.Wire(t), database, sql, rowLimit], ct) : local.RunAsync(t, database, sql, rowLimit, ct);
    }

    internal sealed class RoutingInnerSchemaService(IInnerSchemaService local, INodeRpc rpc) : IInnerSchemaService
    {
        public string Engine => local.Engine;

        public Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget t, string database, CancellationToken ct = default)
            => t.NodeId is { } n ? rpc.InvokeAsync<IReadOnlyList<TableSummary>>(n, "Schema.ListTables", [NodeRouting.Wire(t), database], ct) : local.ListTablesAsync(t, database, ct);

        public Task<TableRows> FetchRowsAsync(InnerDatabaseTarget t, string database, string table, int limit, int offset, CancellationToken ct = default)
            => t.NodeId is { } n ? rpc.InvokeAsync<TableRows>(n, "Schema.FetchRows", [NodeRouting.Wire(t), database, table, limit, offset], ct) : local.FetchRowsAsync(t, database, table, limit, offset, ct);

        public Task UpdateRowAsync(InnerDatabaseTarget t, string database, string table, UpdateRowRequest request, CancellationToken ct = default)
            => t.NodeId is { } n ? rpc.InvokeAsync<bool>(n, "Schema.UpdateRow", [NodeRouting.Wire(t), database, table, request], ct) : local.UpdateRowAsync(t, database, table, request, ct);

        public Task BulkUpdateRowsAsync(InnerDatabaseTarget t, string database, string table, BulkUpdateRowsRequest request, CancellationToken ct = default)
            => t.NodeId is { } n ? rpc.InvokeAsync<bool>(n, "Schema.BulkUpdateRows", [NodeRouting.Wire(t), database, table, request], ct) : local.BulkUpdateRowsAsync(t, database, table, request, ct);

        public Task InsertRowAsync(InnerDatabaseTarget t, string database, string table, InsertRowRequest request, CancellationToken ct = default)
            => t.NodeId is { } n ? rpc.InvokeAsync<bool>(n, "Schema.InsertRow", [NodeRouting.Wire(t), database, table, request], ct) : local.InsertRowAsync(t, database, table, request, ct);

        public Task UploadObjectAsync(InnerDatabaseTarget t, string database, string key, Stream content, string? contentType, CancellationToken ct = default)
            => t.NodeId is not null
                ? throw new NotSupportedException("Object upload is not supported on remote nodes yet.")
                : local.UploadObjectAsync(t, database, key, content, contentType, ct);

        public Task DeleteRowAsync(InnerDatabaseTarget t, string database, string table, DeleteRowRequest request, CancellationToken ct = default)
            => t.NodeId is { } n ? rpc.InvokeAsync<bool>(n, "Schema.DeleteRow", [NodeRouting.Wire(t), database, table, request], ct) : local.DeleteRowAsync(t, database, table, request, ct);

        public Task DropTableAsync(InnerDatabaseTarget t, string database, string table, CancellationToken ct = default)
            => t.NodeId is { } n ? rpc.InvokeAsync<bool>(n, "Schema.DropTable", [NodeRouting.Wire(t), database, table], ct) : local.DropTableAsync(t, database, table, ct);
    }
}
