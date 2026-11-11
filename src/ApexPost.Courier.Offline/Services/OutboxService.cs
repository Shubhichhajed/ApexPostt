using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Offline;

/// <summary>
/// Interface for uploading synced scans to the server.
/// </summary>
public interface IShipmentUploader
{
    /// <summary>
    /// Uploads scans to the server.
    /// Returns a result indicating accepted, skipped, and failed count.
    /// </summary>
    Task<UploadResult> UploadScansAsync(IEnumerable<ShipmentScan> scans, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a scan upload attempt.
/// </summary>
public enum ScanSyncOutcome
{
    Accepted,
    Duplicate,
    Rejected
}

public class UploadResult
{
    public Dictionary<string, ScanSyncOutcome> Outcomes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> RejectionReasonsByScanId { get; set; } = new(StringComparer.Ordinal);

    // Counts are derived from per-scan outcomes so a result cannot silently drift out of sync.
    public int AcceptedCount => Outcomes.Values.Count(x => x == ScanSyncOutcome.Accepted);
    public int DuplicatesSkippedCount => Outcomes.Values.Count(x => x == ScanSyncOutcome.Duplicate);
    public int RejectedCount => Outcomes.Values.Count(x => x == ScanSyncOutcome.Rejected);
    public List<string> RejectionReasons => RejectionReasonsByScanId.Select(x => $"{x.Key}: {x.Value}").ToList();
    public int TotalProcessed => Outcomes.Count;

    /// <summary>
    /// Set when the upload stopped part-way (for example the network dropped or the circuit opened).
    /// Scans without an outcome stay Pending and are retried later.
    /// </summary>
    public string? IncompleteReason { get; set; }
}

/// <summary>
/// Manages the outbox of pending scans for synchronization with the server.
/// Implements the Outbox Pattern for offline persistence.
/// </summary>
public class OutboxService
{
    private readonly IScanRepository _repository;
    private readonly IShipmentUploader _uploader;
    private readonly ILogger<OutboxService>? _logger;

    public OutboxService(IScanRepository repository, IShipmentUploader uploader, ILogger<OutboxService>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _uploader = uploader ?? throw new ArgumentNullException(nameof(uploader));
        _logger = logger;
    }

    /// <summary>Default number of scans sent per upload call when connectivity returns.</summary>
    public const int DefaultBatchSize = 20;

    /// <summary>
    /// Processes all pending scans in a single upload call (used by the Part A console harness).
    /// </summary>
    public Task<UploadResult> SyncPendingScansAsync(CancellationToken cancellationToken = default)
        => SyncPendingScansAsync(int.MaxValue, cancellationToken);

    /// <summary>
    /// Processes pending scans in batches. Each batch is uploaded and its per-scan outcomes are
    /// written back before the next batch starts, so a dropped connection never loses progress.
    /// </summary>
    public async Task<UploadResult> SyncPendingScansAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        var pendingList = (await _repository.GetPendingScansAsync()).ToList();
        var total = new UploadResult();
        if (pendingList.Count == 0)
        {
            _logger?.LogInformation("No pending scans to sync");
            return total;
        }

        _logger?.LogInformation("Syncing {Count} pending scans in batches of up to {BatchSize}",
            pendingList.Count, Math.Min(batchSize, pendingList.Count));

        foreach (var batch in pendingList.Chunk(Math.Min(batchSize, pendingList.Count)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchResult = await ProcessBatchAsync(batch, cancellationToken);
            foreach (var outcome in batchResult.Outcomes)
                total.Outcomes[outcome.Key] = outcome.Value;
            foreach (var reason in batchResult.RejectionReasonsByScanId)
                total.RejectionReasonsByScanId[reason.Key] = reason.Value;

            if (batchResult.IncompleteReason is not null)
            {
                // No point sending the next batch while the server is unreachable.
                total.IncompleteReason = batchResult.IncompleteReason;
                _logger?.LogWarning("Sync stopped early: {Reason}", batchResult.IncompleteReason);
                break;
            }
        }

        _logger?.LogInformation(
            "Sync completed: {Accepted} accepted, {Skipped} duplicates skipped, {Failed} rejected",
            total.AcceptedCount, total.DuplicatesSkippedCount, total.RejectedCount);
        return total;
    }

    private async Task<UploadResult> ProcessBatchAsync(IReadOnlyList<ShipmentScan> batch, CancellationToken cancellationToken)
    {
        UploadResult uploadResult;
        try
        {
            uploadResult = await _uploader.UploadScansAsync(batch, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Network failure, open circuit or expired session: nothing is marked, scans stay Pending.
            _logger?.LogWarning(ex, "Upload of {Count} scans failed; they remain Pending for a later retry", batch.Count);
            throw;
        }

        // Outcomes are matched by scan ID, never by list position.
        var knownIds = batch.Select(scan => scan.Id).ToHashSet(StringComparer.Ordinal);
        var unknownIds = uploadResult.Outcomes.Keys.Where(id => !knownIds.Contains(id)).ToList();
        if (unknownIds.Count > 0)
            throw new InvalidOperationException($"Uploader returned outcomes for unknown scan IDs: {string.Join(", ", unknownIds)}");

        foreach (var scan in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!uploadResult.Outcomes.TryGetValue(scan.Id, out var outcome))
            {
                // An incomplete response is treated as "unknown": keep Pending, the scan ID makes a retry safe.
                _logger?.LogWarning("No upload outcome returned for scan {ScanId}; leaving it Pending", scan.Id);
                continue;
            }

            scan.SyncStatus = outcome == ScanSyncOutcome.Rejected ? SyncStatus.Failed : SyncStatus.Synced;
            await _repository.UpdateAsync(scan);

            if (outcome == ScanSyncOutcome.Rejected)
                _logger?.LogWarning("Marked scan {ScanId} as Failed: {Reason}", scan.Id,
                    uploadResult.RejectionReasonsByScanId.GetValueOrDefault(scan.Id, "Rejected by server"));
            else
                _logger?.LogInformation("Marked scan {ScanId} as Synced ({Outcome})", scan.Id, outcome);
        }

        return uploadResult;
    }

    /// <summary>
    /// Gets the count of pending scans awaiting synchronization.
    /// </summary>
    public async Task<int> GetPendingScanCountAsync()
    {
        try
        {
            var pendingScans = await _repository.GetPendingScansAsync();
            return pendingScans.Count();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting pending scan count");
            return 0;
        }
    }

    /// <summary>
    /// Retries a scan that previously failed to sync.
    /// </summary>
    public async Task<bool> RetryScanAsync(string scanId, CancellationToken cancellationToken = default)
    {
        try
        {
            var scan = await _repository.GetByIdAsync(scanId);
            if (scan == null)
            {
                _logger?.LogWarning("Scan {ScanId} not found for retry", scanId);
                return false;
            }

            if (scan.SyncStatus == SyncStatus.Failed)
            {
                scan.SyncStatus = SyncStatus.Pending;
                await _repository.UpdateAsync(scan);
                _logger?.LogInformation("Reset scan {ScanId} status to Pending for retry", scanId);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error retrying scan {ScanId}", scanId);
            return false;
        }
    }
}
