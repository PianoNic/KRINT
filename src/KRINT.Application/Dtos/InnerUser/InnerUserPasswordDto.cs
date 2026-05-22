namespace KRINT.Application.Dtos.InnerUser
{
    public record InnerUserPasswordDto
    {
        public required string Name { get; init; }
        public required string Password { get; init; }
    }
}
