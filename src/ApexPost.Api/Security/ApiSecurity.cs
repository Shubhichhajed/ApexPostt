using System.Security.Claims;
using ApexPost.Api.Models;

namespace ApexPost.Api.Security;

/// <summary>Role names carried in the JWT role claim.</summary>
public static class ApiRoles
{
    public const string Courier = "Courier";
    public const string Admin = "Admin";
}

/// <summary>Claim types issued by the identity provider (or the Development token endpoint).</summary>
public static class ApiClaims
{
    public const string CourierId = "courierId";
}

/// <summary>A courier may only upload scans recorded under their own courierId claim.</summary>
public static class CourierOwnership
{
    public static bool OwnsAll(ClaimsPrincipal user, ScanUploadRequest request)
    {
        var courierId = user.FindFirst(ApiClaims.CourierId)?.Value;
        return !string.IsNullOrWhiteSpace(courierId)
            && request.Scans.All(scan => string.Equals(scan.CourierId, courierId, StringComparison.Ordinal));
    }
}

/// <summary>
/// Endpoint metadata: marks an endpoint as partner-only. <see cref="PartnerApiKeyMiddleware"/> reads this
/// marker after routing, so the check applies only where the attribute is placed.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequirePartnerApiKeyAttribute : Attribute
{
}
