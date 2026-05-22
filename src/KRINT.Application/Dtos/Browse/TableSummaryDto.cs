namespace KRINT.Application.Dtos.Browse
{
    public record TableSummaryDto
    {
        public required string Name { get; init; }
        public required string Kind { get; init; }
    }
}
