using System.Text.Json;
using ApexPost.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ApexPost.Infrastructure.Data;

/// <summary>EF change listener: records safe aggregate-change envelopes in the same SaveChanges transaction.</summary>
public sealed class ChangeTrackingOutboxInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Capture(DbContext? context)
    {
        if (context is null) return;
        context.ChangeTracker.DetectChanges();
        var changes = context.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(entry => entry.Entity is Branch or Staff or Product or StockItem or CounterTransaction or Payment or MoneyOrder or Shipment)
            .ToArray();
        foreach (var entry in changes)
        {
            var id = entry.Property("Id").CurrentValue?.ToString();
            if (string.IsNullOrWhiteSpace(id)) continue;
            var branchCode = entry.Properties.FirstOrDefault(x => x.Metadata.Name == "BranchCode")?.CurrentValue?.ToString();
            var payload = JsonSerializer.Serialize(new
            {
                AggregateId = id,
                AggregateType = entry.Metadata.ClrType.Name,
                Change = entry.State.ToString(),
                BranchCode = branchCode,
                OccurredAtUtc = DateTimeOffset.UtcNow
            });
            var aggregateType = entry.Metadata.ClrType.Name;
            var eventType = $"{aggregateType}.{entry.State}";
            var pendingDuplicate = context.Set<OutboxMessage>().Local.Any(message =>
                context.Entry(message).State == EntityState.Added &&
                message.AggregateType == aggregateType && message.AggregateId == id && message.EventType == eventType);
            if (pendingDuplicate) continue; // SaveChanges retry: do not enqueue the same event twice in one unit of work.
            context.Set<OutboxMessage>().Add(new OutboxMessage
            {
                AggregateType = aggregateType,
                AggregateId = id,
                EventType = eventType,
                PayloadJson = payload
            });
        }
    }
}
