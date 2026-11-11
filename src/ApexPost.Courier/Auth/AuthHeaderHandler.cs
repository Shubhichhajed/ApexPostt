using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Auth;

/// <summary>Raised when the API rejects the token, so the app can return to the Login screen.</summary>
public sealed class SessionEvents
{
    public event EventHandler? SessionExpired;

    public void RaiseSessionExpired() => SessionExpired?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Adds "Authorization: Bearer ..." from the token store to every API request. On a 401 it clears the
/// stored token and raises <see cref="SessionEvents.SessionExpired"/>. The token value is never logged.
/// </summary>
public sealed class AuthHeaderHandler(ITokenStore tokenStore, SessionEvents sessionEvents, ILogger<AuthHeaderHandler> logger)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenStore.GetAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && request.Headers.Authorization is not null)
        {
            logger.LogWarning("API returned 401 for {Path}; the stored token was cleared", request.RequestUri?.AbsolutePath);
            await tokenStore.ClearAsync(cancellationToken);
            sessionEvents.RaiseSessionExpired();
        }

        return response;
    }
}
