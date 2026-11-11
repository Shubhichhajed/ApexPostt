using ApexPost.Api.Services;

namespace ApexPost.Api.Security;

/// <summary>
/// Validates the X-Api-Key header for endpoints marked with <see cref="RequirePartnerApiKeyAttribute"/>.
/// Missing or wrong key: 401. Key not configured on the server: 500 (fail closed, never fail open).
/// The supplied key is never logged.
/// </summary>
public sealed class PartnerApiKeyMiddleware(RequestDelegate next, ILogger<PartnerApiKeyMiddleware> logger)
{
    public const string HeaderName = "X-Api-Key";

    public async Task InvokeAsync(HttpContext context, IPartnerApiKeyValidator validator)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<RequirePartnerApiKeyAttribute>() is null)
        {
            await next(context);
            return;
        }

        if (!validator.IsConfigured)
        {
            logger.LogError("Partner:ApiKey is not configured; rejecting partner request to {Path}", context.Request.Path);
            await Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "Partner access is not configured",
                detail: "The server has no partner API key configured.").ExecuteAsync(context);
            return;
        }

        if (!validator.IsValid(context.Request.Headers[HeaderName].ToString()))
        {
            logger.LogWarning("Rejected partner request to {Path}: missing or invalid API key", context.Request.Path);
            context.Response.Headers.WWWAuthenticate = $"ApiKey header=\"{HeaderName}\"";
            await Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid or missing partner API key",
                detail: $"Send a valid key in the {HeaderName} header.").ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}
