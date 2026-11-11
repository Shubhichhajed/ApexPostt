using System.Globalization;
using Couchbase.Lite;
using Couchbase.Lite.Query;
using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Offline;

/// <summary>
/// Couchbase Lite implementation of <see cref="IScanRepository"/>.
/// Key-value operations use the document ID directly; list/search operations use parameterised SQL++.
/// Couchbase types never leave this class, so the domain model stays storage-agnostic.
/// </summary>
public sealed class CouchbaseScanRepository : IScanRepository
{
    // "_" is the SQL++ alias for the default collection of the open database.
    private const string SelectScans = "SELECT * FROM _";

    private readonly CourierDatabase _database;
    private readonly ILogger<CouchbaseScanRepository>? _logger;

    public CouchbaseScanRepository(CourierDatabase database, ILogger<CouchbaseScanRepository>? logger = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger;
    }

    public Task<ShipmentScan> CreateAsync(ShipmentScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (!ScanDocumentId.IsValid(scan.Id))
            throw new ArgumentException("Scan ID must use scan::{Guid} format.", nameof(scan));

        // Local-first rule: every new scan starts life in the outbox as Pending.
        scan.SyncStatus = SyncStatus.Pending;
        scan.ScannedAtUtc = scan.ScannedAtUtc.ToUniversalTime();

        var collection = ScanCollection;
        if (collection.GetDocument(scan.Id) is not null)
            throw new InvalidOperationException($"Scan {scan.Id} already exists.");

        var document = new MutableDocument(scan.Id);
        WriteFields(document, scan);
        collection.Save(document);

        _logger?.LogInformation("Saved scan {ScanId} for {TrackingNumber} locally as Pending", scan.Id, scan.TrackingNumber);
        return Task.FromResult(scan);
    }

    public Task<ShipmentScan?> GetByIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Task.FromResult<ShipmentScan?>(null);

        // Key-value lookup: no query engine involved.
        using var document = ScanCollection.GetDocument(id);
        return Task.FromResult(document is null ? null : ReadScan(document, document.Id));
    }

    public Task<ShipmentScan> UpdateAsync(ShipmentScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);

        var collection = ScanCollection;
        using var existing = collection.GetDocument(scan.Id)
            ?? throw new KeyNotFoundException($"Scan {scan.Id} not found.");

        using var document = existing.ToMutable();
        WriteFields(document, scan);
        collection.Save(document);

        _logger?.LogDebug("Updated scan {ScanId} ({SyncStatus})", scan.Id, scan.SyncStatus);
        return Task.FromResult(scan);
    }

    public Task DeleteAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Task.CompletedTask;

        var collection = ScanCollection;
        using var document = collection.GetDocument(id);
        if (document is not null)
        {
            collection.Delete(document);
            _logger?.LogInformation("Deleted scan {ScanId}", id);
        }

        return Task.CompletedTask;
    }

    public Task<IEnumerable<ShipmentScan>> ListAllAsync()
        => Task.FromResult<IEnumerable<ShipmentScan>>(RunScanQuery($"{SelectScans} ORDER BY scannedAtUtc ASC", null));

    public Task<IEnumerable<ShipmentScan>> GetScansByTrackingNumberAsync(string trackingNumber)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber))
            return Task.FromResult(Enumerable.Empty<ShipmentScan>());

        var scans = RunScanQuery(
            $"{SelectScans} WHERE trackingNumber = $trackingNumber ORDER BY scannedAtUtc ASC",
            new Parameters().SetString("trackingNumber", trackingNumber.Trim()));
        return Task.FromResult<IEnumerable<ShipmentScan>>(scans);
    }

    public Task<IEnumerable<ShipmentScan>> GetPendingScansAsync()
    {
        var scans = RunScanQuery(
            $"{SelectScans} WHERE syncStatus = $syncStatus ORDER BY scannedAtUtc ASC",
            new Parameters().SetString("syncStatus", nameof(SyncStatus.Pending)));
        return Task.FromResult<IEnumerable<ShipmentScan>>(scans);
    }

    public Task<Dictionary<ScanType, int>> GetScanCountByCourierAsync(string courierId)
    {
        var counts = new Dictionary<ScanType, int>();
        if (string.IsNullOrWhiteSpace(courierId))
            return Task.FromResult(counts);

        using var query = _database.Database.CreateQuery(
            "SELECT scanType, COUNT(*) AS scanCount FROM _ WHERE courierId = $courierId GROUP BY scanType ORDER BY scanType");
        query.Parameters = new Parameters().SetString("courierId", courierId.Trim());

        using var results = query.Execute();
        foreach (var row in results)
        {
            if (Enum.TryParse<ScanType>(row.GetString("scanType"), ignoreCase: false, out var scanType))
                counts[scanType] = row.GetInt("scanCount");
        }

        return Task.FromResult(counts);
    }

    public Task<IEnumerable<ShipmentScan>> GetScansByCourierAsync(string courierId)
    {
        if (string.IsNullOrWhiteSpace(courierId))
            return Task.FromResult(Enumerable.Empty<ShipmentScan>());

        var scans = RunScanQuery(
            $"{SelectScans} WHERE courierId = $courierId ORDER BY scannedAtUtc DESC",
            new Parameters().SetString("courierId", courierId.Trim()));
        return Task.FromResult<IEnumerable<ShipmentScan>>(scans);
    }

    private Collection ScanCollection => _database.DefaultCollection
        ?? throw new InvalidOperationException("The courier database has not been initialised.");

    /// <summary>Runs a SQL++ statement whose only projection is the whole document (SELECT *).</summary>
    private List<ShipmentScan> RunScanQuery(string sql, Parameters? parameters)
    {
        using var query = _database.Database.CreateQuery(sql);
        if (parameters is not null)
            query.Parameters = parameters;

        using var results = query.Execute();
        var scans = new List<ShipmentScan>();
        foreach (var row in results)
        {
            var body = row.GetDictionary(0);
            if (body is not null)
                scans.Add(ReadScan(body, fallbackId: null));
        }

        return scans;
    }

    // Field names are camelCase so the stored JSON matches the document model in the brief.
    private static void WriteFields(MutableDocument document, ShipmentScan scan)
    {
        document.SetString("id", scan.Id);
        document.SetString("type", "ShipmentScan");
        document.SetString("trackingNumber", scan.TrackingNumber.Trim());
        document.SetString("scanType", scan.ScanType.ToString());
        document.SetString("courierId", scan.CourierId.Trim());
        document.SetString("location", scan.Location.Trim());
        document.SetString("scannedAtUtc", scan.ScannedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        document.SetString("notes", scan.Notes);
        document.SetString("syncStatus", scan.SyncStatus.ToString());
    }

    private static ShipmentScan ReadScan(IDictionaryObject source, string? fallbackId)
    {
        var scannedAt = DateTime.TryParse(source.GetString("scannedAtUtc"), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : DateTime.MinValue;

        return new ShipmentScan
        {
            Id = source.GetString("id") ?? fallbackId ?? string.Empty,
            TrackingNumber = source.GetString("trackingNumber") ?? string.Empty,
            ScanType = Enum.TryParse<ScanType>(source.GetString("scanType"), out var scanType) ? scanType : ScanType.Pickup,
            CourierId = source.GetString("courierId") ?? string.Empty,
            Location = source.GetString("location") ?? string.Empty,
            ScannedAtUtc = scannedAt,
            Notes = source.GetString("notes"),
            SyncStatus = Enum.TryParse<SyncStatus>(source.GetString("syncStatus"), out var status) ? status : SyncStatus.Pending
        };
    }
}
