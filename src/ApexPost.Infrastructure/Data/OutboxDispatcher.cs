using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace ApexPost.Infrastructure.Data;

/// <summary>Development-safe simulated dispatcher. A production transport can replace the log sink.</summary>
public sealed class OutboxDispatcher(IDbContextFactory<ApexPostDbContext> factory, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var db = await factory.CreateDbContextAsync(stoppingToken);
                var pending = await db.OutboxMessages.Where(x => x.ProcessedAtUtc == null)
                    .OrderBy(x => x.OccurredAtUtc).Take(50).ToListAsync(stoppingToken);
                foreach (var message in pending)
                {
                    try
                    {
                        // Simulated downstream publication. Consumers must deduplicate by OutboxMessage.Id.
                        logger.LogInformation("Outbox published {MessageId} {EventType} aggregate={AggregateId}", message.Id, message.EventType, message.AggregateId);
                        message.ProcessedAtUtc = DateTimeOffset.UtcNow;
                        message.AttemptCount++;
                        message.LastError = null;
                    }
                    catch (Exception exception)
                    {
                        message.AttemptCount++;
                        message.LastError = exception.GetType().Name;
                        logger.LogError(exception, "Outbox delivery failed for {MessageId}", message.Id);
                    }
                }
                if (pending.Count > 0) await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Outbox polling cycle failed."); }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
