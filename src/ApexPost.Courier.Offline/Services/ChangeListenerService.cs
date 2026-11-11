using Couchbase.Lite;
using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Offline;

/// <summary>
/// Manages collection change notifications for real-time updates.
/// </summary>
public sealed class ChangeListenerService : IDisposable
{
    private readonly CourierDatabase _database;
    private readonly IScanRepository _repository;
    private readonly ILogger<ChangeListenerService>? _logger;
    // Kept with a separate flag so the code does not depend on whether ListenerToken is a class or a struct.
    private ListenerToken _listenerToken = default!;
    private bool _isListening;
    private readonly object _lockObject = new();

    public ChangeListenerService(CourierDatabase database, IScanRepository repository,
        ILogger<ChangeListenerService>? logger = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger;
        _database.Disposing += StopListening;
    }

    /// <summary>
    /// Registers a change listener that logs changes and tracks pending scans count.
    /// </summary>
    public event EventHandler<ChangeEventArgs>? DocumentChanged;

    /// <summary>Most recent pending-scan count seen by the listener (the live "pending scans" figure).</summary>
    public int LastPendingCount => Volatile.Read(ref _lastPendingCount);

    private int _lastPendingCount;

    /// <summary>Recalculates the pending count on demand, for example when a screen is first shown.</summary>
    public async Task<int> RefreshPendingCountAsync()
    {
        var count = (await _repository.GetPendingScansAsync()).Count();
        Volatile.Write(ref _lastPendingCount, count);
        return count;
    }

    /// <summary>
    /// Represents the arguments for a change event.
    /// </summary>
    public class ChangeEventArgs : EventArgs
    {
        public IReadOnlyList<string> ChangedDocumentIds { get; set; } = new List<string>();
        public int PendingScanCount { get; set; }
    }

    /// <summary>
    /// Starts listening for database changes.
    /// </summary>
    public async Task StartListeningAsync()
    {
        lock (_lockObject)
        {
            if (_isListening)
            {
                _logger?.LogWarning("Change listener already active");
                return;
            }

            var collection = _database.DefaultCollection;
            if (collection == null)
            {
                _logger?.LogError("Default collection not available for listener");
                return;
            }

            // Keep the Couchbase callback short. Querying the collection is dispatched away
            // from the database notification thread to avoid blocking/deadlocking it.
            _listenerToken = collection.AddChangeListener((_, args) =>
            {
                var changedIds = args?.DocumentIDs?.ToArray() ?? Array.Empty<string>();
                _ = PublishChangeAsync(changedIds);
            });
            _isListening = true;

            _logger?.LogInformation("Change listener registered for collection");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops listening for database changes and removes the listener token.
    /// </summary>
    public void StopListening()
    {
        lock (_lockObject)
        {
            if (!_isListening)
                return;

            var collection = _database.DefaultCollection;
            if (collection != null)
            {
                try
                {
                    // Newer Couchbase Lite releases also expose ListenerToken.Remove(); the collection
                    // overload is kept so the code compiles against every 3.x package.
#pragma warning disable CS0618
                    collection.RemoveChangeListener(_listenerToken);
#pragma warning restore CS0618
                    _logger?.LogInformation("Change listener removed");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error removing change listener");
                }
            }

            _listenerToken = default!;
            _isListening = false;
        }
    }

    private async Task PublishChangeAsync(IReadOnlyList<string> changedIds)
    {
        try
        {
            _logger?.LogInformation("Collection changed: {Count} documents modified: {Ids}",
                changedIds.Count, string.Join(", ", changedIds));
            var pendingCount = await RefreshPendingCountAsync();
            DocumentChanged?.Invoke(this, new ChangeEventArgs
            {
                ChangedDocumentIds = changedIds,
                PendingScanCount = pendingCount
            });
            _logger?.LogInformation("Current pending scans count: {Count}", pendingCount);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing change notification");
        }
    }

    /// <summary>
    /// Cleans up the listener when the service is disposed.
    /// </summary>
    public void Dispose()
    {
        StopListening();
        _database.Disposing -= StopListening;
    }
}
