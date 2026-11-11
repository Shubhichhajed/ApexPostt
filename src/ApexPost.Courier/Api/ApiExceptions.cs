using System.Net;

namespace ApexPost.Courier.Api;

/// <summary>An error response from the API, already turned into a message a courier can understand.</summary>
public sealed class ApiProblemException(HttpStatusCode statusCode, ProblemDetailsDto? problem)
    : Exception(ApiErrorMessages.Friendly(statusCode, problem))
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public ProblemDetailsDto? Problem { get; } = problem;

    /// <summary>
    /// Client errors that will not succeed on a resend (bad data, wrong courier, unknown shipment).
    /// The scans in that request are marked Failed instead of being retried forever.
    /// </summary>
    public bool IsPermanent => StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden
        or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity;
}

/// <summary>The token is missing, invalid or expired; the user must log in again.</summary>
public sealed class SessionExpiredException()
    : Exception("Your session has expired. Please log in again.");

/// <summary>The API answered with a success code but the body could not be read.</summary>
public sealed class MalformedResponseException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Builds short, friendly messages from ProblemDetails / ValidationProblemDetails.</summary>
public static class ApiErrorMessages
{
    public static string Friendly(HttpStatusCode statusCode, ProblemDetailsDto? problem)
    {
        var firstValidationError = problem?.Errors?
            .SelectMany(entry => entry.Value ?? Array.Empty<string>())
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));

        var reason = firstValidationError ?? problem?.Detail ?? problem?.Title;
        var prefix = statusCode switch
        {
            HttpStatusCode.BadRequest => "The server rejected the data",
            HttpStatusCode.Unauthorized => "Please log in again",
            HttpStatusCode.Forbidden => "You are not allowed to do that",
            HttpStatusCode.NotFound => "Not found",
            HttpStatusCode.Conflict => "That change conflicts with the shipment's current state",
            _ when (int)statusCode >= 500 => "The server had a problem",
            _ => $"Request failed ({(int)statusCode})"
        };

        return string.IsNullOrWhiteSpace(reason) ? prefix + "." : $"{prefix}: {reason}";
    }
}
