using ApexPost.Courier.Offline;
using Xunit;

namespace ApexPost.UnitTests;

/// <summary>Tests the offline outbox without opening a native Couchbase Lite database.</summary>
public sealed class OutboxServiceTests
{
    [Theory]
    [InlineData(ScanSyncOutcome.Accepted, SyncStatus.Synced)]
    [InlineData(ScanSyncOutcome.Duplicate, SyncStatus.Synced)]
    [InlineData(ScanSyncOutcome.Rejected, SyncStatus.Failed)]
    public async Task Sync_maps_each_explicit_server_outcome_to_the_correct_local_status(
        ScanSyncOutcome outcome, SyncStatus expectedStatus)
    {
        var scan = NewScan();
        var repository = new FakeScanRepository(scan);
        var uploader = new FakeUploader(scans => new UploadResult
        {
            Outcomes = new Dictionary<string, ScanSyncOutcome> { [scan.Id] = outcome },
            RejectionReasonsByScanId = outcome == ScanSyncOutcome.Rejected
                ? new Dictionary<string, string> { [scan.Id] = "Invalid delivery proof" }
                : new Dictionary<string, string>()
        });

        var result = await new OutboxService(repository, uploader).SyncPendingScansAsync();

        Assert.Equal(expectedStatus, scan.SyncStatus);
        Assert.Equal(1, result.TotalProcessed);
        Assert.Equal(1, repository.UpdateCount);
    }

    [Fact]
    public async Task Empty_outbox_does_not_call_uploader()
    {
        var repository = new FakeScanRepository();
        var uploader = new FakeUploader(_ => throw new InvalidOperationException("Must not upload an empty batch."));
        var result = await new OutboxService(repository, uploader).SyncPendingScansAsync();
        Assert.Equal(0, result.TotalProcessed);
        Assert.Equal(0, uploader.CallCount);
    }

    [Fact]
    public async Task Missing_outcome_keeps_scan_pending_for_safe_retry()
    {
        var scan = NewScan();
        var repository = new FakeScanRepository(scan);
        var uploader = new FakeUploader(_ => new UploadResult());
        await new OutboxService(repository, uploader).SyncPendingScansAsync();
        Assert.Equal(SyncStatus.Pending, scan.SyncStatus);
        Assert.Equal(0, repository.UpdateCount);
    }

    [Fact]
    public async Task Unknown_outcome_id_is_rejected_instead_of_corrupting_another_scan()
    {
        var scan = NewScan();
        var repository = new FakeScanRepository(scan);
        var uploader = new FakeUploader(_ => new UploadResult
        {
            Outcomes = new Dictionary<string, ScanSyncOutcome> { ["scan::unknown"] = ScanSyncOutcome.Accepted }
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OutboxService(repository, uploader).SyncPendingScansAsync());
        Assert.Equal(SyncStatus.Pending, scan.SyncStatus);
        Assert.Equal(0, repository.UpdateCount);
    }

    [Fact]
    public async Task Retry_failed_scan_returns_it_to_pending()
    {
        var scan = NewScan();
        scan.SyncStatus = SyncStatus.Failed;
        var repository = new FakeScanRepository(scan);
        var result = await new OutboxService(repository, new FakeUploader(_ => new UploadResult())).RetryScanAsync(scan.Id);
        Assert.True(result);
        Assert.Equal(SyncStatus.Pending, scan.SyncStatus);
        Assert.Equal(1, repository.UpdateCount);
    }

    [Fact]
    public async Task Retry_unknown_scan_returns_false()
    {
        var service = new OutboxService(new FakeScanRepository(), new FakeUploader(_ => new UploadResult()));
        Assert.False(await service.RetryScanAsync("scan::missing"));
    }

    [Fact]
    public async Task Retry_synced_scan_does_not_change_its_status()
    {
        var scan = NewScan();
        scan.SyncStatus = SyncStatus.Synced;
        var repository = new FakeScanRepository(scan);
        Assert.True(await new OutboxService(repository, new FakeUploader(_ => new UploadResult())).RetryScanAsync(scan.Id));
        Assert.Equal(SyncStatus.Synced, scan.SyncStatus);
        Assert.Equal(0, repository.UpdateCount);
    }

    [Fact]
    public async Task Pending_scans_are_uploaded_in_batches_of_twenty()
    {
        var scans = Enumerable.Range(0, 45).Select(_ => NewScan()).ToArray();
        var repository = new FakeScanRepository(scans);
        var batchSizes = new List<int>();
        var uploader = new FakeUploader(batch =>
        {
            var list = batch.ToList();
            batchSizes.Add(list.Count);
            return new UploadResult { Outcomes = list.ToDictionary(x => x.Id, _ => ScanSyncOutcome.Accepted) };
        });

        var result = await new OutboxService(repository, uploader).SyncPendingScansAsync(OutboxService.DefaultBatchSize);

        Assert.Equal(new[] { 20, 20, 5 }, batchSizes);
        Assert.Equal(45, result.AcceptedCount);
        Assert.All(scans, scan => Assert.Equal(SyncStatus.Synced, scan.SyncStatus));
    }

    [Fact]
    public async Task Interrupted_upload_stops_remaining_batches_and_leaves_scans_pending()
    {
        var scans = Enumerable.Range(0, 30).Select(_ => NewScan()).ToArray();
        var repository = new FakeScanRepository(scans);
        var uploader = new FakeUploader(_ => new UploadResult { IncompleteReason = "Server unavailable (circuit open)" });

        var result = await new OutboxService(repository, uploader).SyncPendingScansAsync(20);

        Assert.Equal(1, uploader.CallCount);
        Assert.Equal("Server unavailable (circuit open)", result.IncompleteReason);
        Assert.All(scans, scan => Assert.Equal(SyncStatus.Pending, scan.SyncStatus));
        Assert.Equal(0, repository.UpdateCount);
    }

    [Fact]
    public async Task Uploader_exception_propagates_and_scans_stay_pending()
    {
        var scan = NewScan();
        var repository = new FakeScanRepository(scan);
        var uploader = new FakeUploader(_ => throw new HttpRequestException("No route to host"));

        await Assert.ThrowsAsync<HttpRequestException>(() => new OutboxService(repository, uploader).SyncPendingScansAsync(20));

        Assert.Equal(SyncStatus.Pending, scan.SyncStatus);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Batch_size_must_be_positive(int batchSize)
    {
        var service = new OutboxService(new FakeScanRepository(), new FakeUploader(_ => new UploadResult()));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SyncPendingScansAsync(batchSize));
    }

    [Theory]
    [InlineData("scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301", true)]
    [InlineData("scan::not-a-guid", false)]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301", false)]
    [InlineData("SCAN::3f2504e0-4f89-11d3-9a0c-0305e82c3301", false)]
    [InlineData(null, false)]
    public void Scan_document_ids_use_the_required_format(string? id, bool expected)
    {
        Assert.Equal(expected, ScanDocumentId.IsValid(id));
        Assert.True(ScanDocumentId.IsValid(new ShipmentScan().Id));
    }

    private static ShipmentScan NewScan() => new()
    {
        Id = $"scan::{Guid.NewGuid():D}", TrackingNumber = "APX-12345678", ScanType = ScanType.Pickup,
        CourierId = "courier-001", Location = "Indore Hub", ScannedAtUtc = DateTime.UtcNow,
        SyncStatus = SyncStatus.Pending
    };

    private sealed class FakeUploader(Func<IEnumerable<ShipmentScan>, UploadResult> handler) : IShipmentUploader
    {
        public int CallCount { get; private set; }
        public Task<UploadResult> UploadScansAsync(IEnumerable<ShipmentScan> scans, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(handler(scans));
        }
    }

    private sealed class FakeScanRepository(params ShipmentScan[] seed) : IScanRepository
    {
        private readonly List<ShipmentScan> _scans = seed.ToList();
        public int UpdateCount { get; private set; }
        public Task<ShipmentScan> CreateAsync(ShipmentScan scan) { _scans.Add(scan); return Task.FromResult(scan); }
        public Task<ShipmentScan?> GetByIdAsync(string id) => Task.FromResult(_scans.SingleOrDefault(x => x.Id == id));
        public Task<ShipmentScan> UpdateAsync(ShipmentScan scan) { UpdateCount++; return Task.FromResult(scan); }
        public Task DeleteAsync(string id) { _scans.RemoveAll(x => x.Id == id); return Task.CompletedTask; }
        public Task<IEnumerable<ShipmentScan>> ListAllAsync() => Task.FromResult<IEnumerable<ShipmentScan>>(_scans.ToArray());
        public Task<IEnumerable<ShipmentScan>> GetScansByTrackingNumberAsync(string trackingNumber)
            => Task.FromResult<IEnumerable<ShipmentScan>>(_scans.Where(x => x.TrackingNumber == trackingNumber).ToArray());
        public Task<IEnumerable<ShipmentScan>> GetPendingScansAsync()
            => Task.FromResult<IEnumerable<ShipmentScan>>(_scans.Where(x => x.SyncStatus == SyncStatus.Pending).ToArray());
        public Task<Dictionary<ScanType, int>> GetScanCountByCourierAsync(string courierId)
            => Task.FromResult(_scans.Where(x => x.CourierId == courierId).GroupBy(x => x.ScanType).ToDictionary(x => x.Key, x => x.Count()));
        public Task<IEnumerable<ShipmentScan>> GetScansByCourierAsync(string courierId)
            => Task.FromResult<IEnumerable<ShipmentScan>>(_scans.Where(x => x.CourierId == courierId).ToArray());
    }
}
