namespace KRINT.Application.Dtos.InnerUser
{
    /// <summary>Body for setting a password. Null/empty Password means "generate one".</summary>
    public record SetPasswordDto
    {
        public string? Password { get; init; }
    }
}
