using ApexPost.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Api.Middleware;

/// <summary>
/// Global IExceptionHandler. Known failures become RFC 7807 ProblemDetails with the right status code;
/// anything else is a 500 whose message and stack trace are only shown in Development.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment) : IExceptionHandler
{
    /// <summary>Non-standard but widely used status for "client closed the request".</summary>
    public const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            // The caller went away; this is not a server fault, so log quietly and write no body.
            logger.LogInformation("Request to {Path} was cancelled by the client", context.Request.Path);
            context.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        var (status, title) = exception switch
        {
            ShipmentNotFoundException => (StatusCodes.Status404NotFound, "Shipment not found"),
            ShipmentConflictException => (StatusCodes.Status409Conflict, "Shipment conflict"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The resource was changed by another request"),
            BadHttpRequestException bad => (bad.StatusCode, "Bad request"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        if (status >= 500)
            logger.LogError(exception, "Unhandled API exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            logger.LogWarning("API request {Method} {Path} rejected with {StatusCode}: {Reason}",
                context.Request.Method, context.Request.Path, status, exception.GetType().Name);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://httpstatuses.io/{status}",
            Instance = context.Request.Path
        };
        // Client errors carry a safe, rule-level message; server errors only reveal details in Development.
        if (status < 500 || environment.IsDevelopment())
            problem.Detail = exception.Message;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null,
            "application/problem+json", cancellationToken);
        return true;
    }
}
