using System.Security.Claims;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.API
{
    /// <summary>HttpContext-backed implementation. Reads the preferred_username from the bearer
    /// token, falling back to name + email. Registered as Scoped so it picks up the per-request
    /// HttpContext.</summary>
    public class HttpCurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
    {
        public string? GetActorName()
        {
            var user = accessor.HttpContext?.User;
            return user?.FindFirstValue("preferred_username")
                ?? user?.FindFirstValue(ClaimTypes.Name)
                ?? user?.FindFirstValue(ClaimTypes.Email);
        }
    }
}
