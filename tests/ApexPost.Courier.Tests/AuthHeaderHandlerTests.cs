using System.Net;
using ApexPost.Courier.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApexPost.Courier.Tests;

public sealed class AuthHeaderHandlerTests
{
    [Fact]
    public async Task Adds_the_stored_token_as_a_bearer_header()
    {
        var stub = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = Create(new InMemoryTokenStore("aaa.bbb.ccc"), new SessionEvents(), stub);

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.test/api/v2/shipments/APX-12345678"), CancellationToken.None);

        Assert.Equal("Bearer aaa.bbb.ccc", Assert.Single(stub.AuthorizationHeaders));
    }

    [Fact]
    public async Task Sends_no_authorization_header_when_logged_out()
    {
        var stub = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = Create(new InMemoryTokenStore(null), new SessionEvents(), stub);

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.test/health"), CancellationToken.None);

        Assert.Null(Assert.Single(stub.AuthorizationHeaders));
    }

    [Fact]
    public async Task A_401_clears_the_token_and_raises_session_expired()
    {
        var store = new InMemoryTokenStore("expired.token.value");
        var events = new SessionEvents();
        var raised = 0;
        events.SessionExpired += (_, _) => raised++;
        var stub = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var invoker = Create(store, events, stub);

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.test/api/v2/shipments"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(store.Token);
        Assert.Equal(1, store.ClearCalls);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task A_403_keeps_the_token()
    {
        var store = new InMemoryTokenStore("valid.token.value");
        var stub = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var invoker = Create(store, new SessionEvents(), stub);

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://api.test/api/v2/shipments/APX-12345678/scans"), CancellationToken.None);

        Assert.Equal("valid.token.value", store.Token);
    }

    private static HttpMessageInvoker Create(ITokenStore store, SessionEvents events, HttpMessageHandler inner) =>
        new(new AuthHeaderHandler(store, events, NullLogger<AuthHeaderHandler>.Instance) { InnerHandler = inner });
}
