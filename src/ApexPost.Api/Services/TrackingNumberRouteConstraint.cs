using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ApexPost.Api.Services;

/// <summary>Restricts tracking-number route values to APX- followed by exactly eight digits.</summary>
public sealed class TrackingNumberRouteConstraint : IRouteConstraint
{
    private static readonly Regex Pattern = new("^APX-[0-9]{8}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey,
        RouteValueDictionary values, RouteDirection routeDirection)
    {
        return values.TryGetValue(routeKey, out var value) &&
               value is not null && Pattern.IsMatch(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
    }
}
