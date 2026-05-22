using KRINT.Application.Dtos.Browse;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Mappings.Browse
{
    public static class TableRowsMappings
    {
        public static TableSummaryDto ToDto(this TableSummary t) => new()
        {
            Name = t.Name,
            Kind = t.Kind,
        };

        public static TableRowsDto ToDto(this TableRows rows) => new()
        {
            Columns = rows.Columns,
            Rows = rows.Rows,
            TotalCount = rows.TotalCount,
        };
    }
}
