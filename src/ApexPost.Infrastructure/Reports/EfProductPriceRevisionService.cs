using ApexPost.Application;
using ApexPost.Domain;
using ApexPost.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Reports;

public sealed class EfProductPriceRevisionService(ApexPostDbContext db) : IProductPriceRevisionService
{
    public async Task<int> ReviseCategoryPricesAsync(ProductCategory category, decimal multiplier, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
        if (multiplier is < 0.01m or > 5m) throw new ArgumentOutOfRangeException(nameof(multiplier), "Price multiplier must be between 0.01 and 5.00.");
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var now = DateTimeOffset.UtcNow;
                var affected = await db.Products.Where(x => x.Category == category && x.IsActive)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.UnitPrice, x => Math.Round(x.UnitPrice * multiplier, 2))
                        .SetProperty(x => EF.Property<DateTimeOffset?>(x, "ModifiedAtUtc"), (DateTimeOffset?)now), cancellationToken);
                db.OutboxMessages.Add(new OutboxMessage
                {
                    AggregateType = "ProductCategory",
                    AggregateId = category.ToString(),
                    EventType = "ProductCategory.PricesRevised",
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { Category = category.ToString(), Multiplier = multiplier, AffectedProducts = affected })
                });
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return affected;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        });
    }
}
