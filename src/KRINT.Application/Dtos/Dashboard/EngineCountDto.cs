namespace KRINT.Application.Dtos.Dashboard
{
    public record EngineCountDto
    {
        public required string Engine { get; init; }
        public required int Count { get; init; }
    }
}
