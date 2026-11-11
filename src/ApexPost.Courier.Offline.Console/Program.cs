using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ApexPost.Courier.Offline;

// Setup DI
var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

services.AddSingleton<CourierDatabase>();
services.AddSingleton<IScanRepository, CouchbaseScanRepository>();
services.AddSingleton<IShipmentUploader, FakeShipmentUploader>();
services.AddSingleton<OutboxService>();
services.AddSingleton<ChangeListenerService>();

using var serviceProvider = services.BuildServiceProvider();
var database = serviceProvider.GetRequiredService<CourierDatabase>();
var repository = serviceProvider.GetRequiredService<IScanRepository>();
var outboxService = serviceProvider.GetRequiredService<OutboxService>();
var changeListener = serviceProvider.GetRequiredService<ChangeListenerService>();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

// Initialize database
try
{
    await database.InitializeAsync();
    await changeListener.StartListeningAsync();

    // Subscribe to change events
    changeListener.DocumentChanged += (sender, args) =>
    {
        Console.WriteLine($"\n[listener] {args.ChangedDocumentIds.Count} documents changed: {string.Join(", ", args.ChangedDocumentIds)}");
        Console.WriteLine($"[listener] Pending scans count: {args.PendingScanCount}");
        Console.WriteLine();
    };

    logger.LogInformation("Database initialized successfully");
}
catch (Exception ex)
{
    logger.LogError(ex, "Failed to initialize database");
    return;
}

// Console harness menu
while (true)
{
    Console.WriteLine("\n=== ApexPost Courier Offline Console ===");
    Console.WriteLine("1. Add Scan");
    Console.WriteLine("2. Find by Tracking Number");
    Console.WriteLine("3. List Pending");
    Console.WriteLine("4. Simulate Sync");
    Console.WriteLine("5. Delete Scan");
    Console.WriteLine("6. Exit");
    Console.WriteLine("7. Scan Counts by Courier (SQL++ GROUP BY)");
    Console.Write("\nSelect option: ");

    var option = Console.ReadLine();
    Console.WriteLine();

    try
    {
        switch (option)
        {
            case "1":
                await AddScanMenu(repository);
                break;
            case "2":
                await FindByTrackingNumberMenu(repository);
                break;
            case "3":
                await ListPendingMenu(repository);
                break;
            case "4":
                await SimulateSyncMenu(outboxService);
                break;
            case "5":
                await DeleteScanMenu(repository);
                break;
            case "6":
                changeListener.StopListening();
                database.Dispose();
                logger.LogInformation("Goodbye!");
                return;
            case "7":
                await CountsByCourierMenu(repository);
                break;
            default:
                Console.WriteLine("Invalid option");
                break;
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error executing option");
    }
}

async Task AddScanMenu(IScanRepository repo)
{
    Console.Write("Tracking Number (e.g., APX-12345678): ");
    var trackingNumber = Console.ReadLine() ?? string.Empty;

    Console.Write("Scan Type (Pickup/InTransit/Delivered/Failed): ");
    var scanTypeStr = Console.ReadLine() ?? "Pickup";

    if (!Enum.TryParse<ScanType>(scanTypeStr, out var scanType))
    {
        Console.WriteLine("Invalid scan type");
        return;
    }

    Console.Write("Courier ID: ");
    var courierId = Console.ReadLine() ?? string.Empty;

    Console.Write("Location: ");
    var location = Console.ReadLine() ?? string.Empty;

    Console.Write("Notes (optional): ");
    var notes = Console.ReadLine();

    var scan = new ShipmentScan
    {
        TrackingNumber = trackingNumber,
        ScanType = scanType,
        CourierId = courierId,
        Location = location,
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
        ScannedAtUtc = DateTime.UtcNow,
        SyncStatus = SyncStatus.Pending
    };

    var created = await repo.CreateAsync(scan);
    Console.WriteLine($"Scan created with ID: {created.Id}");
}

async Task FindByTrackingNumberMenu(IScanRepository repo)
{
    Console.Write("Tracking Number: ");
    var trackingNumber = Console.ReadLine() ?? string.Empty;

    var scans = await repo.GetScansByTrackingNumberAsync(trackingNumber);
    var scanList = scans.ToList();

    if (!scanList.Any())
    {
        Console.WriteLine("No scans found for this tracking number");
        return;
    }

    Console.WriteLine($"\nFound {scanList.Count} scans:");
    foreach (var scan in scanList)
    {
        Console.WriteLine($"  ID: {scan.Id}");
        Console.WriteLine($"  Type: {scan.ScanType}");
        Console.WriteLine($"  Location: {scan.Location}");
        Console.WriteLine($"  Time: {scan.ScannedAtUtc:O}");
        Console.WriteLine($"  Status: {scan.SyncStatus}");
        Console.WriteLine($"  Notes: {scan.Notes ?? "N/A"}");
        Console.WriteLine();
    }
}

async Task ListPendingMenu(IScanRepository repo)
{
    var pendingScans = await repo.GetPendingScansAsync();
    var pendingList = pendingScans.ToList();

    if (!pendingList.Any())
    {
        Console.WriteLine("No pending scans");
        return;
    }

    Console.WriteLine($"Found {pendingList.Count} pending scans:");
    foreach (var scan in pendingList)
    {
        Console.WriteLine($"  ID: {scan.Id}");
        Console.WriteLine($"  Tracking: {scan.TrackingNumber}");
        Console.WriteLine($"  Type: {scan.ScanType}");
        Console.WriteLine($"  Courier: {scan.CourierId}");
        Console.WriteLine($"  Time: {scan.ScannedAtUtc:O}");
        Console.WriteLine();
    }
}

async Task SimulateSyncMenu(OutboxService outbox)
{
    Console.WriteLine("Syncing pending scans with fake uploader...");
    var result = await outbox.SyncPendingScansAsync();

    Console.WriteLine($"Sync completed:");
    Console.WriteLine($"  Accepted: {result.AcceptedCount}");
    Console.WriteLine($"  Duplicates Skipped: {result.DuplicatesSkippedCount}");
    Console.WriteLine($"  Rejected: {result.RejectedCount}");

    if (result.RejectionReasons.Any())
    {
        Console.WriteLine("  Rejection Reasons:");
        foreach (var reason in result.RejectionReasons)
        {
            Console.WriteLine($"    - {reason}");
        }
    }
}

async Task CountsByCourierMenu(IScanRepository repo)
{
    Console.Write("Courier ID: ");
    var courierId = Console.ReadLine() ?? string.Empty;
    var counts = await repo.GetScanCountByCourierAsync(courierId);
    if (counts.Count == 0)
    {
        Console.WriteLine("No scans recorded for this courier");
        return;
    }

    Console.WriteLine($"Scans per type for {courierId}:");
    foreach (var (scanType, count) in counts)
        Console.WriteLine($"  {scanType,-10} {count}");
}

async Task DeleteScanMenu(IScanRepository repo)
{
    Console.Write("Scan ID to delete: ");
    var scanId = Console.ReadLine() ?? string.Empty;

    var scan = await repo.GetByIdAsync(scanId);
    if (scan == null)
    {
        Console.WriteLine("Scan not found");
        return;
    }

    await repo.DeleteAsync(scanId);
    Console.WriteLine($"Scan {scanId} deleted");
}

/// <summary>
/// Fake uploader for testing the outbox pattern without a real API.
/// </summary>
public class FakeShipmentUploader : IShipmentUploader
{
    private readonly ILogger<FakeShipmentUploader> _logger;
    private readonly Random _random = new();

    public FakeShipmentUploader(ILogger<FakeShipmentUploader> logger)
    {
        _logger = logger;
    }

    public async Task<UploadResult> UploadScansAsync(IEnumerable<ShipmentScan> scans,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(500, cancellationToken); // Simulate network delay

        var scanList = scans.ToList();
        var result = new UploadResult();

        // Simulate random success/failure
        foreach (var scan in scanList)
        {
            var rand = _random.Next(100);

            if (rand < 80) // 80% success
            {
                result.Outcomes[scan.Id] = ScanSyncOutcome.Accepted;
                _logger.LogInformation("Uploaded scan {ScanId}", scan.Id);
            }
            else if (rand < 90) // 10% duplicate
            {
                result.Outcomes[scan.Id] = ScanSyncOutcome.Duplicate;
                _logger.LogInformation("Skipped duplicate scan {ScanId}", scan.Id);
            }
            else // 10% failure
            {
                result.Outcomes[scan.Id] = ScanSyncOutcome.Rejected;
                result.RejectionReasonsByScanId[scan.Id] = "Simulated validation failure";
                _logger.LogWarning("Failed to upload scan {ScanId}", scan.Id);
            }
        }

        return result;
    }
}
