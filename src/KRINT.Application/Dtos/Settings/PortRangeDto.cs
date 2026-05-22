namespace KRINT.Application.Dtos.Settings
{
    public record PortRangeDto
    {
        public required string Engine { get; init; }
        public required int Start { get; init; }
        public required int End { get; init; }
    }
}
