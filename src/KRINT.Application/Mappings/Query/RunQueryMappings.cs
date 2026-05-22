using KRINT.Application.Dtos.Query;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Mappings.Query
{
    public static class RunQueryMappings
    {
        public static RunQueryResultDto ToDto(this QueryResult result) => new()
        {
            Columns = result.Columns.Select(c => new RunQueryColumnDto(c.Name, c.TypeName)).ToList(),
            Rows = result.Rows,
            RowsAffected = result.RowsAffected,
            ElapsedMs = result.ElapsedMs,
            Truncated = result.Truncated,
        };
    }
}
