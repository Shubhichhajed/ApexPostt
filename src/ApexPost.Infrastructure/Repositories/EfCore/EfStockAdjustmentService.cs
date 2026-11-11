using ApexPost.Application;
using ApexPost.Domain;
using ApexPost.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Repositories.EfCore;

public sealed class EfStockAdjustmentService(ApexPostDbContext db) : IStockAdjustmentService
{
    public async Task AdjustAsync(string branchCode, string sku, int delta, string reason, string changedBy, CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"EXEC dbo.usp_AdjustStock @BranchCode={branchCode}, @Sku={sku}, @Delta={delta}, @Reason={reason}, @ChangedBy={changedBy}",
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (SqlException exception) when (exception.Number is >= 51005 and <= 51010)
            {
                throw new DomainException(exception.Message);
            }
            catch
            {
                try { await transaction.RollbackAsync(CancellationToken.None); }
                catch (InvalidOperationException) { /* The procedure may already have rolled back the outer transaction. */ }
                throw;
            }
        });
    }
}
