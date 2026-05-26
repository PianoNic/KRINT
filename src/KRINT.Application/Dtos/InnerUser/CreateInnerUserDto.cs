namespace KRINT.Application.Dtos.InnerUser
{
    public record CreateInnerUserDto
    {
        public required string Name { get; init; }
        /// <summary>Custom password. Null or empty triggers auto-generation.</summary>
        public string? Password { get; init; }
    }
}
