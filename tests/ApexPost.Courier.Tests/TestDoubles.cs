using System.Net;
using System.Text;
using ApexPost.Courier.Auth;

namespace ApexPost.Courier.Tests;

/// <summary>Records each request (method, path, Authorization header) and answers with a scripted response.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);
    public List<string?> AuthorizationHeaders { get; } = [];
    public List<string> Paths { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _calls);
        lock (Paths)
        {
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
            Paths.Add(request.RequestUri!.AbsolutePath);
        }
        return Task.FromResult(respond(request, call));
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) };
}

internal sealed class InMemoryTokenStore(string? token) : ITokenStore
{
    public string? Token { get; private set; } = token;
    public int ClearCalls { get; private set; }

    public Task<string?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(Token);

    public Task SaveAsync(string token, CancellationToken cancellationToken = default)
    {
        Token = token;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Token = null;
        ClearCalls++;
        return Task.CompletedTask;
    }
}
