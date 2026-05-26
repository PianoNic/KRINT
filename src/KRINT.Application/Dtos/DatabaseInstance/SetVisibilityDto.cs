namespace KRINT.Application.Dtos.DatabaseInstance
{
    public record SetVisibilityDto
    {
        /// <summary>True to publish on 0.0.0.0 (LAN-visible), false to bind to 127.0.0.1.</summary>
        public required bool IsPublic { get; init; }
    }
}
