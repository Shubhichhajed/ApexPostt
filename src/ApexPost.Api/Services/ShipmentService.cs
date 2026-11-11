using ApexPost.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Api.Services;

/// <summary>Business operations for shipments. Controllers contain no business rules.</summary>
public sealed class ShipmentService : IShipmentService
{
    private static readonly HashSet<string> ScanTypes = new(StringComparer.OrdinalIgnoreCase)
        { "Pickup", "InTransit", "Delivered", "Failed" };
    private static readonly IReadOnlyDictionary<string, string[]> AllowedTransitions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Created"] = ["PickedUp"],
        ["PickedUp"] = ["InTransit"],
        ["InTransit"] = ["OutForDelivery"],
        ["OutForDelivery"] = ["Delivered", "Failed"]
    };

    private readonly ApexPost.Infrastructure.Data.ApexPostDbContext _dbContext;
    private readonly ShipmentMetadataStore _metadata;
    private readonly ILogger<ShipmentService> _logger;

    public ShipmentService(ApexPost.Infrastructure.Data.ApexPostDbContext dbContext,
        ShipmentMetadataStore metadata, ILogger<ShipmentService> logger)
    {
        _dbContext = dbContext;
        _metadata = metadata;
        _logger = logger;
    }

    public async Task<(List<ShipmentDTO> items, int total)> GetShipmentsAsync(int page, int pageSize, string? status, string? city,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(page), "Page must be positive and pageSize must be between 1 and 50.");

        IQueryable<Shipment> query = _dbContext.Shipments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) || !string.IsNullOrWhiteSpace(city))
        {
            var matchingTrackingNumbers = await _metadata.GetTrackingNumbersAsync(status, city, cancellationToken);
            query = query.Where(x => matchingTrackingNumbers.Contains(x.Id));
        }

        var total = await query.CountAsync(cancellationToken);
        var pageEntities = await query.OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var metadata = await _metadata.GetByTrackingNumbersAsync(pageEntities.Select(x => x.Id).ToArray(), cancellationToken);
        return (pageEntities.Select(x => Map(x, metadata.GetValueOrDefault(x.Id))).ToList(), total);
    }

    public async Task<ShipmentDTO?> GetShipmentAsync(string trackingNumber, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Shipments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == trackingNumber, cancellationToken);
        if (entity is null) return null;
        return Map(entity, await _metadata.GetAsync(trackingNumber, cancellationToken));
    }

    public async Task<ShipmentDTO> CreateShipmentAsync(string trackingNumber, decimal weight, string recipientName,
        string recipientPhone, string recipientAddress, string recipientCity, string? notes,
        CancellationToken cancellationToken = default)
    {
        if (await _dbContext.Shipments.AnyAsync(x => x.Id == trackingNumber, cancellationToken))
            throw new ShipmentConflictException($"Shipment {trackingNumber} already exists.");

        var branchCode = await _dbContext.Branches.AsNoTracking().Select(x => x.Code).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No branch is configured. Seed at least one branch before creating shipments.");
        var rate = await _dbContext.ShippingRates.AsNoTracking()
            .Where(x => x.ServiceLevel == ServiceLevel.Standard && x.MaximumWeightKg >= weight)
            .OrderBy(x => x.Zone)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException($"No Standard shipping rate is configured for a {weight:0.###} kg parcel.");

        var shipment = new Shipment
        {
            Id = trackingNumber, WeightKg = weight, Kind = ShipmentKind.Parcel,
            ServiceLevel = rate.ServiceLevel, DestinationZone = rate.Zone,
            LengthCm = 1m, WidthCm = 1m, HeightCm = 1m,
            Price = rate.BaseRate + (weight * rate.PerKgRate), CreatedAt = DateTimeOffset.UtcNow,
            BranchCode = branchCode
        };
        _dbContext.Shipments.Add(shipment);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await _dbContext.Shipments.AsNoTracking().AnyAsync(x => x.Id == trackingNumber, cancellationToken))
                throw new ShipmentConflictException($"Shipment {trackingNumber} already exists.");
            throw;
        }

        var data = new ShipmentMetadata
        {
            TrackingNumber = trackingNumber, Status = "Created", RecipientName = recipientName.Trim(),
            RecipientPhone = recipientPhone.Trim(), RecipientAddress = recipientAddress.Trim(),
            RecipientCity = recipientCity.Trim(), Notes = notes
        };
        await _metadata.SaveAsync(data, cancellationToken);
        _logger.LogInformation("Created shipment {TrackingNumber}", trackingNumber);
        return Map(shipment, data);
    }

    public async Task<ShipmentDTO> UpdateShipmentAsync(string trackingNumber, decimal weight, string recipientName,
        string recipientPhone, string recipientAddress, string recipientCity, string? notes,
        CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Shipments.SingleOrDefaultAsync(x => x.Id == trackingNumber, cancellationToken)
            ?? throw new ShipmentNotFoundException($"Shipment {trackingNumber} was not found.");
        var data = await _metadata.GetAsync(trackingNumber, cancellationToken) ?? new ShipmentMetadata
        {
            TrackingNumber = trackingNumber, RecipientCity = shipment.DestinationZone
        };
        if (!string.Equals(data.Status, "Created", StringComparison.OrdinalIgnoreCase))
            throw new ShipmentConflictException("Shipment details can only be replaced while status is Created.");

        var rate = await _dbContext.ShippingRates.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Zone == shipment.DestinationZone && x.ServiceLevel == shipment.ServiceLevel && x.MaximumWeightKg >= weight,
            cancellationToken)
            ?? throw new ArgumentException($"No configured shipping rate supports a {weight:0.###} kg parcel for this shipment zone.");
        shipment.WeightKg = weight;
        shipment.Price = rate.BaseRate + (weight * rate.PerKgRate);
        data.RecipientName = recipientName.Trim();
        data.RecipientPhone = recipientPhone.Trim();
        data.RecipientAddress = recipientAddress.Trim();
        data.RecipientCity = recipientCity.Trim();
        data.Notes = notes;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _metadata.SaveAsync(data, cancellationToken);
        return Map(shipment, data);
    }

    public async Task<ShipmentDTO> UpdateShipmentStatusAsync(string trackingNumber, string newStatus,
        CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Shipments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == trackingNumber, cancellationToken)
            ?? throw new ShipmentNotFoundException($"Shipment {trackingNumber} was not found.");
        var data = await _metadata.GetAsync(trackingNumber, cancellationToken) ?? new ShipmentMetadata
        {
            TrackingNumber = trackingNumber, RecipientCity = shipment.DestinationZone
        };
        if (!AllowedTransitions.TryGetValue(data.Status, out var allowed) || !allowed.Contains(newStatus, StringComparer.OrdinalIgnoreCase))
            throw new ShipmentConflictException($"Status transition from {data.Status} to {newStatus} is not allowed.");
        data.Status = allowed.First(x => string.Equals(x, newStatus, StringComparison.OrdinalIgnoreCase));
        await _metadata.SaveAsync(data, cancellationToken);
        return Map(shipment, data);
    }

    public async Task DeleteShipmentAsync(string trackingNumber, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Shipments.SingleOrDefaultAsync(x => x.Id == trackingNumber, cancellationToken)
            ?? throw new ShipmentNotFoundException($"Shipment {trackingNumber} was not found.");
        var data = await _metadata.GetAsync(trackingNumber, cancellationToken);
        if (data is not null && !string.Equals(data.Status, "Created", StringComparison.OrdinalIgnoreCase))
            throw new ShipmentConflictException("A shipment can only be deleted while its status is Created.");
        _dbContext.Shipments.Remove(shipment);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _metadata.DeleteAsync(trackingNumber, cancellationToken);
    }

    public async Task<ScanUploadResult> UploadScansAsync(string trackingNumber, List<OfflineScanUpload> scans,
        CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.Shipments.AsNoTracking().AnyAsync(x => x.Id == trackingNumber, cancellationToken);
        if (!exists) throw new ShipmentNotFoundException($"Shipment {trackingNumber} was not found.");
        var existingMetadata = await _metadata.GetAsync(trackingNumber, cancellationToken);
        if (existingMetadata is null)
        {
            var shipment = await _dbContext.Shipments.AsNoTracking().SingleAsync(x => x.Id == trackingNumber, cancellationToken);
            await _metadata.SaveAsync(new ShipmentMetadata { TrackingNumber = trackingNumber, RecipientCity = shipment.DestinationZone }, cancellationToken);
        }

        var result = new ScanUploadResult();
        foreach (var scan in scans)
        {
            var reason = ValidateScan(scan);
            if (reason is not null)
            {
                result.Rejected++;
                result.RejectionReasons.Add($"{scan.ScanId}: {reason}");
                continue;
            }

            var insertResult = await _metadata.TryAddScanAsync(trackingNumber, new ApiScanRecord
            {
                ScanId = scan.ScanId, ScanType = scan.ScanType, CourierId = scan.CourierId,
                Location = scan.Location, ScannedAtUtc = scan.ScannedAtUtc.ToUniversalTime(), Notes = scan.Notes
            }, cancellationToken);
            switch (insertResult)
            {
                case ScanInsertResult.Inserted:
                    result.Accepted++;
                    break;
                case ScanInsertResult.Duplicate:
                    result.DuplicatesSkipped++;
                    break;
                case ScanInsertResult.IdBelongsToAnotherShipment:
                    result.Rejected++;
                    result.RejectionReasons.Add($"{scan.ScanId}: scan ID is already associated with another shipment.");
                    break;
                default:
                    result.Rejected++;
                    result.RejectionReasons.Add($"{scan.ScanId}: shipment metadata could not be updated.");
                    break;
            }
        }
        _logger.LogInformation("Scan upload for {TrackingNumber}: accepted={Accepted}, duplicates={Duplicates}, rejected={Rejected}",
            trackingNumber, result.Accepted, result.DuplicatesSkipped, result.Rejected);
        return result;
    }

    public async Task<List<ScanDTO>> GetScansAsync(string trackingNumber, CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Shipments.AsNoTracking().AnyAsync(x => x.Id == trackingNumber, cancellationToken))
            throw new ShipmentNotFoundException($"Shipment {trackingNumber} was not found.");
        var data = await _metadata.GetAsync(trackingNumber, cancellationToken);
        return (data?.Scans ?? []).OrderByDescending(x => x.ScannedAtUtc)
            .Select(x => new ScanDTO { Id = x.ScanId, ScanType = x.ScanType, CourierId = x.CourierId,
                Location = x.Location, ScannedAtUtc = x.ScannedAtUtc, Notes = x.Notes }).ToList();
    }

    private static string? ValidateScan(OfflineScanUpload scan)
    {
        if (string.IsNullOrWhiteSpace(scan.ScanId) || !scan.ScanId.StartsWith("scan::", StringComparison.Ordinal) ||
            !Guid.TryParse(scan.ScanId[6..], out _)) return "ScanId must use scan::{Guid} format.";
        if (!ScanTypes.Contains(scan.ScanType)) return $"Unsupported scan type '{scan.ScanType}'.";
        if (string.IsNullOrWhiteSpace(scan.CourierId)) return "CourierId is required.";
        if (string.IsNullOrWhiteSpace(scan.Location)) return "Location is required.";
        if (scan.ScannedAtUtc == default) return "Scan timestamp is required.";
        if (scan.ScannedAtUtc > DateTime.UtcNow.AddSeconds(5)) return "Scan timestamp cannot be in the future.";
        if (string.Equals(scan.ScanType, "Delivered", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(scan.Notes))
            return "Delivered scans must include recipient signature/name in Notes.";
        return null;
    }

    private static ShipmentDTO Map(Shipment shipment, ShipmentMetadata? data) => new()
    {
        Id = shipment.Id, TrackingNumber = shipment.Id, Status = data?.Status ?? "Created",
        Weight = shipment.WeightKg, RecipientName = data?.RecipientName ?? string.Empty,
        RecipientPhone = data?.RecipientPhone ?? string.Empty, RecipientAddress = data?.RecipientAddress ?? string.Empty,
        RecipientCity = data?.RecipientCity ?? shipment.DestinationZone, Notes = data?.Notes,
        CreatedAtUtc = shipment.CreatedAt.UtcDateTime,
        LatestScan = data?.Scans.OrderByDescending(x => x.ScannedAtUtc).Select(x => new ScanDTO
        { Id = x.ScanId, ScanType = x.ScanType, CourierId = x.CourierId, Location = x.Location, ScannedAtUtc = x.ScannedAtUtc, Notes = x.Notes }).FirstOrDefault()
    };

}
