using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ApexPost.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace ApexPost.Web.Middleware;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireApiKeyAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RestrictedEndpointAttribute : Attribute { }

public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
        var correlationId = !string.IsNullOrWhiteSpace(incoming) && incoming.Length <= 128 && incoming.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.') ? incoming : Guid.NewGuid().ToString("N");
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() => { context.Response.Headers["X-Correlation-Id"] = correlationId; return Task.CompletedTask; });
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }
}

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger, IWebHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { logger.LogInformation("Request cancelled by client. CorrelationId={CorrelationId}", context.TraceIdentifier); }
        catch (Exception exception)
        {
            logger.LogError(exception, "Request failed. CorrelationId={CorrelationId}", context.TraceIdentifier);
            if (context.Response.HasStarted) throw;

            var (status, title, detail) = exception switch
            {
                DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The record was changed by another user.", "Refresh the record and retry the operation."),
                ConcurrencyConflictException conflict => (StatusCodes.Status409Conflict, "The requested inventory is no longer available.", conflict.Message),
                SqlException sqlError when sqlError.Number == 51011 => (StatusCodes.Status403Forbidden, "Stock adjustment authorization is required.", "An active branch manager or administrator must authorize this operation."),
                SqlException sqlError when sqlError.Number is >= 51005 and <= 51010 => (StatusCodes.Status400BadRequest, "The stock adjustment was rejected.", sqlError.Message),
                DbUpdateException update when update.InnerException is SqlException sql && sql.Number is 2601 or 2627
                    => (StatusCodes.Status409Conflict, "A record with the same unique value already exists.", "Check the unique fields and retry."),
                KeyNotFoundException => (StatusCodes.Status404NotFound, "The requested record was not found.", "The record may have been removed."),
                DomainException domain => (StatusCodes.Status400BadRequest, "The business rule rejected this operation.", domain.Message),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", environment.IsDevelopment() ? exception.Message : "The request could not be completed. Contact support with the trace ID.")
            };
            context.Response.Clear(); context.Response.StatusCode = status; context.Response.ContentType = "application/problem+json";
            var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
            problem.Extensions["traceId"] = context.TraceIdentifier;
            await context.Response.WriteAsJsonAsync(JsonSerializer.Serialize(problem), cancellationToken: context.RequestAborted);
        }
    }
}

public sealed class RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew(); var failed = false;
        try { await next(context); }
        catch { failed = true; throw; }
        finally
        {
            stopwatch.Stop();
            var loggedStatus = failed && context.Response.StatusCode < StatusCodes.Status500InternalServerError ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
            logger.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms; Failed={Failed}; CorrelationId={CorrelationId}", context.Request.Method, context.Request.Path, loggedStatus, stopwatch.Elapsed.TotalMilliseconds, failed, context.TraceIdentifier);
        }
    }
}

public sealed class MaintenanceModeMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var enabled = configuration.GetValue<bool>("Maintenance:Enabled");
        if (enabled && !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers["Retry-After"] = configuration["Maintenance:RetryAfterSeconds"] ?? "300";
            await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = 503, Title = "Service temporarily unavailable", Detail = "The platform is undergoing scheduled maintenance." }, cancellationToken: context.RequestAborted);
            return;
        }
        await next(context);
    }
}

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'; style-src 'self'; script-src 'self'";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            return Task.CompletedTask;
        });
        await next(context);
    }
}

public sealed class ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<ApiKeyMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<RequireApiKeyAttribute>() is null) { await next(context); return; }
        var partnerId = context.Request.Headers["X-Partner-Id"].FirstOrDefault();
        partnerId = string.IsNullOrWhiteSpace(partnerId) || partnerId.Length > 80 || partnerId.Any(c => !char.IsLetterOrDigit(c) && c is not '-' and not '_' and not '.') ? "default" : partnerId;
        var partnerSpecificKey = configuration[$"Integration:Partners:{partnerId}:ApiKey"];
        var expected = partnerId == "default" ? partnerSpecificKey ?? configuration["Integration:ApiKey"] : partnerSpecificKey;
        if (string.IsNullOrWhiteSpace(expected))
        {
            logger.LogCritical("Integration API key is not configured for partner {PartnerId}.", partnerId); context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = 500, Title = "Integration endpoint is not configured." }, cancellationToken: context.RequestAborted); return;
        }
        var supplied = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(supplied) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = 401, Title = "A valid API key is required." }, cancellationToken: context.RequestAborted); return;
        }
        context.Items["AuthenticatedPartnerId"] = partnerId;
        await next(context);
    }
}

public sealed class IpRestrictionMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<IpRestrictionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var pathRestricted = context.Request.Path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase) || context.Request.Path.StartsWithSegments("/integration", StringComparison.OrdinalIgnoreCase);
        var metadataRestricted = context.GetEndpoint()?.Metadata.GetMetadata<RestrictedEndpointAttribute>() is not null;
        if (!pathRestricted && !metadataRestricted) { await next(context); return; }
        var allowed = configuration.GetSection("Security:AllowedIps").Get<string[]>() ?? [];
        var remote = context.Connection.RemoteIpAddress;
        static IPAddress NormalizeIp(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4(): address;
        var isAllowed = remote is not null && allowed.Any(value => IPAddress.TryParse(value, out var address) && NormalizeIp(address).Equals(NormalizeIp(remote)));

        if (!isAllowed)
        {
            logger.LogWarning("Blocked request from IP {RemoteIp} to restricted endpoint {Path}", remote?.ToString() ?? "unknown", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = 403, Title = "Access from this network is not permitted." }, cancellationToken: context.RequestAborted); return;
        }

        await next(context);
    }
}
