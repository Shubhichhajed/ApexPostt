using ApexPost.Application;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ApexPost.Infrastructure.Data;

/// <summary>Durable, idempotent inbox for terminal operations; business mutations are reconciled separately.</summary>
public sealed class OfflineSyncService(ApexPostDbContext db) : IOfflineSyncService
{
    public async Task<OfflineSyncResult> AcceptAsync(OfflineSyncCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.ClientId) || command.ClientId.Length > 80) throw new ArgumentException("A valid client ID is required.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.OperationId) || command.OperationId.Length > 80) throw new ArgumentException("A valid idempotency operation ID is required.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.AggregateType) || command.AggregateType.Length > 80) throw new ArgumentException("A valid aggregate type is required.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.PayloadJson) || command.PayloadJson.Length > 32768) throw new ArgumentException("The offline payload is empty or too large.", nameof(command));
        if (command.AggregateType is not ("CounterTransaction" or "StockItem" or "Payment" or "Product" or "MoneyOrder"))
            throw new ArgumentException("This aggregate type is not enabled for offline synchronization.", nameof(command));
        if (command.PayloadJson.Contains("DocumentNumber", StringComparison.OrdinalIgnoreCase)
            || command.PayloadJson.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)
            || command.PayloadJson.Contains("Password", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Offline payloads must not contain document numbers, API keys or passwords.", nameof(command));
        try { using var document = JsonDocument.Parse(command.PayloadJson); }
        catch (JsonException exception) { throw new ArgumentException("Offline payload must be valid JSON.", nameof(command), exception); }
        var existing = await db.SyncInbox.AsNoTracking().FirstOrDefaultAsync(x => x.ClientId == command.ClientId && x.OperationId == command.OperationId, cancellationToken);
        if (existing is not null) return new OfflineSyncResult(command.OperationId, existing.Status, true, existing.ConflictPolicy);

        var record = new SyncInboxRecord
        {
            ClientId = command.ClientId,
            OperationId = command.OperationId,
            AggregateType = command.AggregateType,
            AggregateId = command.AggregateId,
            PayloadJson = command.PayloadJson,
            ClientOccurredAtUtc = command.ClientOccurredAtUtc,
            Status = "Pending",
            ConflictPolicy = command.AggregateType is "StockItem" or "Payment" or "CounterTransaction"
                ? "ServerWinsAndRequireRefresh"
                : "ManualReviewForConflicts"
        };
        db.SyncInbox.Add(record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new OfflineSyncResult(command.OperationId, record.Status, false, record.ConflictPolicy);
        }
        catch (DbUpdateException)
        {
            db.Entry(record).State = EntityState.Detached;
            var duplicate = await db.SyncInbox.AsNoTracking().FirstOrDefaultAsync(x => x.ClientId == command.ClientId && x.OperationId == command.OperationId, cancellationToken);
            if (duplicate is null) throw;
            return new OfflineSyncResult(command.OperationId, duplicate.Status, true, duplicate.ConflictPolicy);
        }
    }
}
