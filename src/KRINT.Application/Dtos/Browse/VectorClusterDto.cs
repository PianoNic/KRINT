namespace KRINT.Application.Dtos.Browse
{
    public record VectorPointDto
    {
        public required string Id { get; init; }
        public required IReadOnlyList<float> Vector { get; init; }
        public required string Payload { get; init; }
    }

    public record VectorClusterDto
    {
        public required int Dimensions { get; init; }
        public required IReadOnlyList<VectorPointDto> Points { get; init; }
    }
}
