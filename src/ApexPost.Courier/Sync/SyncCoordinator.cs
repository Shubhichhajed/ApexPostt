using ApexPost.Courier.Api;
using ApexPost.Courier.Auth;
using ApexPost.Courier.Offline;
using ApexPost.Courier.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;

namespace ApexPost.Courier.Sync;

/// <summary>
/// Runs the outbox: automatically when connectivity returns, after a new scan while online, or on demand.
/// Only one sync runs at a time. Pending scans are uploaded in batches of <see cref="SyncSettings.BatchSize"/>.
/// </summary>
public sealed class SyncCoordinator : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly ITokenStore _tokens;
    private readonly ConnectionState _state;
    private readonly ConnectivityMonitor _connectivity;
    private readonly SyncSettings _settings;
    private readonly ILogger<SyncCoordinator> _logger;
    private readonly SemaphoreSlim _singleRun = new(1, 1);

    public SyncCoordinator(IServiceProvider services, ITokenStore tokens, ConnectionState state,
        ConnectivityMonitor connectivity, IOptions<SyncSettings> settings, ILogger<SyncCoordinator> logger)
    {
        _services = services;
        _tokens = tokens;
        _state = state;
        _connectivity = connectivity;
        _settings = settings.Value;
        _logger = logger;
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    /// <summary>Raised after each sync attempt with a one-line summary for the screen.</summary>
    public event EventHandler<string>? SyncCompleted;

    /// <summary>Starts a background sync if the device looks online. Never blocks the caller.</summary>
    public void TriggerInBackground(string reason)
    {
        if (!_state.IsOnline)
            return;
        _ = Task.Run(() => SyncNowAsync(reason, CancellationToken.None));
    }

    public async Task<string> SyncNowAsync(string reason, CancellationToken cancellationToken)
    {
        if (!await _singleRun.WaitAsync(0, cancellationToken))
            return "A sync is already running.";

        try
        {
            if (string.IsNullOrWhiteSpace(await _tokens.GetAsync(cancellationToken)))
                return Finish("Not logged in: scans stay Pending until you log in.", success: false);

            _logger.LogInformation("Sync started ({Reason})", reason);
            _state.RecordSyncStarted();

            // Transient services: a fresh typed HttpClient from the factory for every run.
            var outbox = _services.GetRequiredService<OutboxService>();
            var result = await outbox.SyncPendingScansAsync(_settings.BatchSize, cancellationToken);

            var summary = $"{result.AcceptedCount} accepted, {result.DuplicatesSkippedCount} duplicates, {result.RejectedCount} rejected";
            return result.IncompleteReason is null
                ? Finish($"Sync complete: {summary}.", success: true)
                : Finish($"Sync incomplete ({summary}): {result.IncompleteReason}. Remaining scans stay Pending.", success: false);
        }
        catch (SessionExpiredException ex)
        {
            return Finish(ex.Message, success: false);
        }
        catch (BrokenCircuitException)
        {
            return Finish($"{ConnectionState.OfflineBanner}. Scans stay Pending.", success: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Background syncs must never crash the app; the scans simply stay Pending.
            return Finish($"Sync failed: {ex.Message} Scans stay Pending.", success: false);
        }
        finally
        {
            _singleRun.Release();
        }
    }

    private string Finish(string message, bool success)
    {
        if (success)
            _state.RecordSyncSucceeded(message);
        else
            _state.RecordSyncFailed(message);

        _logger.LogInformation("{SyncMessage}", message);
        SyncCompleted?.Invoke(this, message);
        return message;
    }

    private void OnConnectivityChanged(object? sender, bool online)
    {
        if (online)
            TriggerInBackground("connectivity restored");
    }

    public void Dispose()
    {
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        _singleRun.Dispose();
    }
}
