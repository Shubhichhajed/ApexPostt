using System.Data;
using ApexPost.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Api.Services;

/// <summary>Persists API-owned shipment metadata and scans in additive SQL tables.</summary>
public sealed class ShipmentMetadataStore(ApexPostApiDbContext dbContext)
{
    public async Task<ShipmentMetadata?> GetAsync(string trackingNumber, CancellationToken cancellationToken = default)
        => await dbContext.ShipmentMetadata.AsNoTracking().Include(x => x.Scans)
            .SingleOrDefaultAsync(x => x.TrackingNumber == trackingNumber, cancellationToken);


    public async Task<List<string>> GetTrackingNumbersAsync(string? status, string? city, CancellationToken cancellationToken = default)
    {
        IQueryable<ShipmentMetadata> query = dbContext.ShipmentMetadata.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(city))
            query = query.Where(x => x.RecipientCity == city);
        return await query.Select(x => x.TrackingNumber).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, ShipmentMetadata>> GetByTrackingNumbersAsync(
        IReadOnlyCollection<string> trackingNumbers, CancellationToken cancellationToken = default)
    {
        if (trackingNumbers.Count == 0)
            return new Dictionary<string, ShipmentMetadata>(StringComparer.OrdinalIgnoreCase);
        var records = await dbContext.ShipmentMetadata.AsNoTracking().Include(x => x.Scans)
            .Where(x => trackingNumbers.Contains(x.TrackingNumber)).ToListAsync(cancellationToken);
        return records.ToDictionary(x => x.TrackingNumber, x => x, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyDictionary<string, ShipmentMetadata>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var records = await dbContext.ShipmentMetadata.AsNoTracking().Include(x => x.Scans).ToListAsync(cancellationToken);
        return records.ToDictionary(x => x.TrackingNumber, x => x, StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveAsync(ShipmentMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var existing = await dbContext.ShipmentMetadata.SingleOrDefaultAsync(
            x => x.TrackingNumber == metadata.TrackingNumber, cancellationToken);
        if (existing is null)
        {
            dbContext.ShipmentMetadata.Add(new ShipmentMetadata
            {
                TrackingNumber = metadata.TrackingNumber,
                Status = metadata.Status,
                RecipientName = metadata.RecipientName,
                RecipientPhone = metadata.RecipientPhone,
                RecipientAddress = metadata.RecipientAddress,
                RecipientCity = metadata.RecipientCity,
                Notes = metadata.Notes
            });
        }
        else
        {
            existing.Status = metadata.Status;
            existing.RecipientName = metadata.RecipientName;
            existing.RecipientPhone = metadata.RecipientPhone;
            existing.RecipientAddress = metadata.RecipientAddress;
            existing.RecipientCity = metadata.RecipientCity;
            existing.Notes = metadata.Notes;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string trackingNumber, CancellationToken cancellationToken = default)
    {
        var metadata = await dbContext.ShipmentMetadata.SingleOrDefaultAsync(
            x => x.TrackingNumber == trackingNumber, cancellationToken);
        if (metadata is null) return;
        dbContext.ShipmentMetadata.Remove(metadata); // Scan history is removed by the configured cascade.
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Inserts a scan once, using a serializable transaction and the scan ID primary key.</summary>
    public async Task<ScanInsertResult> TryAddScanAsync(string trackingNumber, ApiScanRecord scan,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var shipment = await dbContext.ShipmentMetadata.SingleOrDefaultAsync(
            x => x.TrackingNumber == trackingNumber, cancellationToken);
        if (shipment is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ScanInsertResult.ShipmentMetadataMissing;
        }

        var existing = await dbContext.ShipmentScans.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ScanId == scan.ScanId, cancellationToken);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return string.Equals(existing.TrackingNumber, trackingNumber, StringComparison.OrdinalIgnoreCase)
                ? ScanInsertResult.Duplicate
                : ScanInsertResult.IdBelongsToAnotherShipment;
        }

        scan.TrackingNumber = trackingNumber;
        dbContext.ShipmentScans.Add(scan);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ScanInsertResult.Inserted;
        }
        catch (DbUpdateException)
        {
            // The primary key on ScanId is the final idempotency guard if two requests race.
            // Roll back before checking whether the competing request inserted the same ID.
            await transaction.RollbackAsync(cancellationToken);
            dbContext.Entry(scan).State = EntityState.Detached;
            var competingInsert = await dbContext.ShipmentScans.AsNoTracking()
                .SingleOrDefaultAsync(x => x.ScanId == scan.ScanId, cancellationToken);
            if (competingInsert is not null)
            {
                return string.Equals(competingInsert.TrackingNumber, trackingNumber, StringComparison.OrdinalIgnoreCase)
                    ? ScanInsertResult.Duplicate
                    : ScanInsertResult.IdBelongsToAnotherShipment;
            }

            throw;
        }
    }
}

/// <summary>API-owned shipment metadata; the Week 3 Shipment entity remains unchanged.</summary>
public sealed class ShipmentMetadata
{
    public string TrackingNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "Created";
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public string RecipientAddress { get; set; } = string.Empty;
    public string RecipientCity { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public List<ApiScanRecord> Scans { get; set; } = new();
}

/// <summary>API-owned uploaded courier scan.</summary>
public sealed class ApiScanRecord
{
    public string ScanId { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string ScanType { get; set; } = string.Empty;
    public string CourierId { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime ScannedAtUtc { get; set; }
    public string? Notes { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Result of an idempotent scan insertion.</summary>
public enum ScanInsertResult
{
    Inserted,
    Duplicate,
    IdBelongsToAnotherShipment,
    ShipmentMetadataMissing
}

public sealed class ShipmentNotFoundException(string message) : Exception(message) { }
public sealed class ShipmentConflictException(string message) : Exception(message) { }
