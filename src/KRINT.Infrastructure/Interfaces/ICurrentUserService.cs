namespace KRINT.Infrastructure.Interfaces
{
    /// <summary>
    /// Resolves the acting principal for the current request. Infrastructure stays decoupled
    /// from ASP.NET Core; the API layer registers a concrete implementation that reads from
    /// the HttpContext + JWT claims.
    /// </summary>
    public interface ICurrentUserService
    {
        /// <summary>Display name for activity logging - preferred_username, name, or email
        /// from the bearer token. Null for background jobs.</summary>
        string? GetActorName();
    }
}
