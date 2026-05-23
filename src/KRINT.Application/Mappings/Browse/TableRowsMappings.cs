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
            ColumnInfos = rows.ColumnInfos?.Select(c => new ColumnInfoDto
            {
                Name = c.Name,
                Type = c.Type,
                Nullable = c.Nullable,
                IsPrimaryKey = c.IsPrimaryKey,
                IsGenerated = c.IsGenerated,
            }).ToList(),
        };
    }
}
