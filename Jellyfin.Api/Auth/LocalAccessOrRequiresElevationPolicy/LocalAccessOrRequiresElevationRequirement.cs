using Microsoft.AspNetCore.Authorization;

namespace MulletaFlix.Api.Auth.LocalAccessOrRequiresElevationPolicy
{
    /// <summary>
    /// The local access or elevated privileges authorization requirement.
    /// </summary>
    public class LocalAccessOrRequiresElevationRequirement : IAuthorizationRequirement
    {
    }
}

