using ApexPost.Courier.Api;
using ApexPost.Courier.Offline;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ApexPost.Courier.Sync;

/// <summary>
/// The real <see cref="IShipmentUploader"/> (replaces the Part A fake). The API takes scans per tracking
/// number, so a batch is split by tracking number and each group is one POST. Per-scan outcomes:
/// listed in rejectionReasons -> Rejected (Failed locally); otherwise accepted or duplicate -> Synced.
/// </summary>
public sealed class ApiShipmentUploader(ApexApiClient api, ILogger<ApiShipmentUploader> logger) : IShipmentUploader
{
    public async Task<UploadResult> UploadScansAsync(IEnumerable<ShipmentScan> scans, CancellationToken cancellationToken = default)
    {
        var result = new UploadResult();
        foreach (var group in scans.GroupBy(scan => scan.TrackingNumber, StringComparer.OrdinalIgnoreCase))
        {
            var batch = group.ToList();
            try
            {
                var response = await api.UploadScansAsync(group.Key, ToRequest(batch), cancellationToken);
                ApplyResponse(batch, response, result);
                logger.LogInformation("Uploaded {Count} scans for {TrackingNumber}: {Accepted} accepted, {Duplicates} duplicates, {Rejected} rejected",
                    batch.Count, group.Key, response.Accepted, response.DuplicatesSkipped, response.Rejected);
            }
            catch (ApiProblemException ex) when (ex.IsPermanent)
            {
                // The server will never accept this request as sent (validation, wrong courier, unknown shipment).
                foreach (var scan in batch)
                {
                    result.Outcomes[scan.Id] = ScanSyncOutcome.Rejected;
                    result.RejectionReasonsByScanId[scan.Id] = ex.Message;
                }
                logger.LogWarning("Server rejected {Count} scans for {TrackingNumber} with {StatusCode}",
                    batch.Count, group.Key, (int)ex.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException
                                           or ApiProblemException or MalformedResponseException)
            {
                // Transient: keep everything not yet confirmed as Pending and stop this run.
                result.IncompleteReason = ex is BrokenCircuitException
                    ? "Server unavailable (circuit open)"
                    : $"Upload interrupted: {ex.Message}";
                break;
            }
        }

        return result;
    }

    internal static ScanUploadRequestDto ToRequest(IEnumerable<ShipmentScan> scans) => new()
    {
        Scans = scans.Select(scan => new ScanItemDto
        {
            ScanId = scan.Id,
            ScanType = scan.ScanType.ToString(),
            CourierId = scan.CourierId,
            Location = scan.Location,
            ScannedAtUtc = scan.ScannedAtUtc,
            Notes = scan.Notes
        }).ToList()
    };

    internal static void ApplyResponse(IReadOnlyList<ShipmentScan> batch, ScanUploadResponseDto response, UploadResult result)
    {
        var rejected = ParseRejectionReasons(response.RejectionReasons);
        // The API reports counts, not per-ID accepted/duplicate flags; both mean "the server has it".
        var successOutcome = response.Accepted == 0 && response.DuplicatesSkipped > 0
            ? ScanSyncOutcome.Duplicate
            : ScanSyncOutcome.Accepted;

        foreach (var scan in batch)
        {
            if (rejected.TryGetValue(scan.Id, out var reason))
            {
                result.Outcomes[scan.Id] = ScanSyncOutcome.Rejected;
                result.RejectionReasonsByScanId[scan.Id] = reason;
            }
            else
            {
                result.Outcomes[scan.Id] = successOutcome;
            }
        }
    }

    /// <summary>Server reasons look like "scan::{guid}: reason".</summary>
    internal static Dictionary<string, string> ParseRejectionReasons(IEnumerable<string>? reasons)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in reasons ?? Enumerable.Empty<string>())
        {
            var separator = line.IndexOf(": ", StringComparison.Ordinal);
            if (separator > 0)
                parsed[line[..separator]] = line[(separator + 2)..];
        }

        return parsed;
    }
}
