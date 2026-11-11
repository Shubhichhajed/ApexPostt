using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace ApexPost.Courier.Resilience;

/// <summary>
/// Resilience pipeline for every ApexApiClient call (outermost first):
/// total timeout -> retry (exponential backoff + jitter) -> circuit breaker -> per-attempt timeout.
/// </summary>
public static class ApexApiResilience
{
    public const string PipelineName = "apexpost-api";

    /// <summary>Marks a POST/PATCH request as safe to resend (the scan upload is, thanks to scan IDs).</summary>
    public static readonly HttpRequestOptionsKey<bool> IdempotentRequest = new("ApexPost.IdempotentRequest");

    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> pipeline, ResilienceHandlerContext context)
    {
        var settings = context.ServiceProvider.GetRequiredService<IOptions<ResilienceSettings>>().Value;
        var state = context.ServiceProvider.GetRequiredService<ConnectionState>();
        var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ApexPost.Courier.Resilience");

        // 1. Overall budget for one logical call, including every retry and back-off delay.
        pipeline.AddTimeout(TimeSpan.FromSeconds(settings.TotalTimeoutSeconds));

        // 2. Retry: network errors, 408 and 5xx only, and only for idempotent requests. Never a 4xx.
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = settings.MaxRetryAttempts,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromSeconds(settings.BaseDelaySeconds),
            ShouldHandle = args => ValueTask.FromResult(
                IsTransient(args.Outcome) && IsRetryAllowed(args.Context.GetRequestMessage())),
            OnRetry = args =>
            {
                state.RecordRetry();
                var request = args.Context.GetRequestMessage();
                logger.LogWarning("Retry {Attempt} of {MaxAttempts} for {Method} {Path} in {DelayMs} ms after {Reason}",
                    args.AttemptNumber + 1, settings.MaxRetryAttempts, request?.Method, request?.RequestUri?.AbsolutePath,
                    (int)args.RetryDelay.TotalMilliseconds, Describe(args.Outcome));
                return default;
            }
        });

        // 3. Circuit breaker: after repeated failures stop calling the server for a while and work offline.
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = settings.FailureRatio,
            MinimumThroughput = settings.MinimumThroughput,
            SamplingDuration = TimeSpan.FromSeconds(settings.SamplingDurationSeconds),
            BreakDuration = TimeSpan.FromSeconds(settings.BreakDurationSeconds),
            ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome)),
            OnOpened = args =>
            {
                state.SetCircuit(CircuitStatus.Open);
                logger.LogError("Circuit OPEN for {BreakSeconds} s after {Reason}. {Banner}",
                    args.BreakDuration.TotalSeconds, Describe(args.Outcome), ConnectionState.OfflineBanner);
                return default;
            },
            OnHalfOpened = _ =>
            {
                state.SetCircuit(CircuitStatus.HalfOpen);
                logger.LogWarning("Circuit HALF-OPEN: sending a trial request");
                return default;
            },
            OnClosed = _ =>
            {
                state.SetCircuit(CircuitStatus.Closed);
                logger.LogInformation("Circuit CLOSED: server reachable again");
                return default;
            }
        });

        // 4. Per-attempt timeout so one hung connection does not use the whole budget.
        pipeline.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds),
            OnTimeout = args =>
            {
                logger.LogWarning("Attempt timed out after {TimeoutSeconds} s", args.Timeout.TotalSeconds);
                return default;
            }
        });
    }

    /// <summary>Network failure, attempt timeout, 408 or any 5xx. Every other status (including all 4xx) is final.</summary>
    public static bool IsTransient(Outcome<HttpResponseMessage> outcome) => outcome switch
    {
        { Exception: HttpRequestException } => true,
        { Exception: TimeoutRejectedException } => true,
        { Result: { } response } => response.StatusCode == HttpStatusCode.RequestTimeout || (int)response.StatusCode >= 500,
        _ => false
    };

    /// <summary>GET/HEAD/OPTIONS/PUT/DELETE are idempotent by definition; POST/PATCH only when explicitly marked.</summary>
    public static bool IsRetryAllowed(HttpRequestMessage? request)
    {
        if (request is null)
            return false;
        if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head || request.Method == HttpMethod.Options ||
            request.Method == HttpMethod.Put || request.Method == HttpMethod.Delete)
            return true;
        return request.Options.TryGetValue(IdempotentRequest, out var idempotent) && idempotent;
    }

    private static string Describe(Outcome<HttpResponseMessage> outcome) => outcome switch
    {
        { Exception: { } exception } => exception.GetType().Name,
        { Result: { } response } => $"HTTP {(int)response.StatusCode}",
        _ => "unknown outcome"
    };
}
