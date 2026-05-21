namespace KRINT.Application.Dtos
{
    public record InnerUserPasswordDto
    {
        public required string Name { get; init; }
        public required string Password { get; init; }
    }
}
