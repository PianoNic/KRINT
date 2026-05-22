namespace KRINT.Application.Dtos.InnerUser
{
    public record GrantInnerUserDto
    {
        /// <summary>Inner database name to grant the user access to.</summary>
        public required string Database { get; init; }
    }
}
