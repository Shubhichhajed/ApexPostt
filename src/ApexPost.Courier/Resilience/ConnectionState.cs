namespace ApexPost.Courier.Resilience;

public enum CircuitStatus
{
    Closed,
    Open,
    HalfOpen
}

/// <summary>
/// Shared, thread-safe view of the connection: circuit state (set by the resilience callbacks),
/// connectivity (set by the monitor) and the last sync result. The Sync Status screen reads it.
/// </summary>
public sealed class ConnectionState
{
    private readonly Lock _gate = new();
    private CircuitStatus _circuit = CircuitStatus.Closed;
    private bool _isOnline;
    private DateTimeOffset? _lastSyncAttemptUtc;
    private DateTimeOffset? _lastSuccessfulSyncUtc;
    private string? _lastSyncSummary;
    private string? _lastError;
    private int _consecutiveFailures;
    private int _retryCount;

    public const string OfflineBanner = "Server unavailable – working offline";

    public CircuitStatus Circuit { get { lock (_gate) return _circuit; } }
    public bool IsOnline { get { lock (_gate) return _isOnline; } }
    public DateTimeOffset? LastSyncAttemptUtc { get { lock (_gate) return _lastSyncAttemptUtc; } }
    public DateTimeOffset? LastSuccessfulSyncUtc { get { lock (_gate) return _lastSuccessfulSyncUtc; } }
    public string? LastSyncSummary { get { lock (_gate) return _lastSyncSummary; } }
    public string? LastError { get { lock (_gate) return _lastError; } }
    public int ConsecutiveFailures { get { lock (_gate) return _consecutiveFailures; } }
    public int RetryCount { get { lock (_gate) return _retryCount; } }

    /// <summary>True while the circuit is open or the device has no route to the API.</summary>
    public bool ShowOfflineBanner { get { lock (_gate) return _circuit == CircuitStatus.Open || !_isOnline; } }

    public void SetCircuit(CircuitStatus status) { lock (_gate) _circuit = status; }
    public void SetOnline(bool isOnline) { lock (_gate) _isOnline = isOnline; }
    public void RecordRetry() { lock (_gate) _retryCount++; }

    public void RecordSyncStarted() { lock (_gate) _lastSyncAttemptUtc = DateTimeOffset.UtcNow; }

    public void RecordSyncSucceeded(string summary)
    {
        lock (_gate)
        {
            _lastSuccessfulSyncUtc = DateTimeOffset.UtcNow;
            _lastSyncSummary = summary;
            _lastError = null;
            _consecutiveFailures = 0;
        }
    }

    public void RecordSyncFailed(string error)
    {
        lock (_gate)
        {
            _lastError = error;
            _consecutiveFailures++;
        }
    }
}
