using ApexPost.Application;
using ApexPost.Domain;
using ApexPost.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Reports;

/// <summary>Screen read models use server-side projections and no tracking.</summary>
public sealed class EfPlatformQueryService(IDbContextFactory<ApexPostDbContext> factory) : IPlatformQueryService
{
    public async Task<IReadOnlyList<ProductStockReadDto>> GetProductStockAsync(string? branchCode, bool includeAllBranches, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Products.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(product => new ProductStockReadDto(
                product.Sku, product.Name, product.Category, product.UnitPrice, product.TaxCategory,
                db.BranchStock.AsNoTracking()
                    .Where(stock => stock.Sku == product.Sku && (includeAllBranches || stock.BranchCode == branchCode))
                    .Select(stock => (int?)stock.Quantity).Sum() ?? 0))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TransactionListReadDto>> GetTransactionsAsync(string? branchCode, bool includeAllBranches, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.CounterTransactions.AsNoTracking()
            .Where(x => includeAllBranches || x.BranchCode == branchCode)
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .Select(x => new TransactionListReadDto(x.Id, x.BranchCode, x.CreatedAt, x.Lines.Count, x.Total, x.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<TransactionDetailsReadDto?> GetTransactionDetailsAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var header = await db.CounterTransactions.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new TransactionDetailsReadDto(x.Id, x.BranchCode, x.CounterId, x.ClerkId,
                Array.Empty<TransactionLineReadDto>(), x.Subtotal, x.Tax, x.Total, x.PaymentMethod,
                x.Status, x.Reason, x.CreatedAt, x.CompletedAt))
            .SingleOrDefaultAsync(cancellationToken);
        if (header is null) return null;

        var lines = await db.CounterTransactions.AsNoTracking().Where(x => x.Id == id)
            .SelectMany(x => x.Lines)
            .Select(line => new TransactionLineReadDto(line.Id, line.Kind, line.Description, line.Sku,
                line.Quantity, line.UnitPrice, line.TaxRate, line.LineTotal))
            .ToListAsync(cancellationToken);
        return header with { Lines = lines };
    }

    public async Task<IReadOnlyList<IdentityVerificationSummaryDto>> GetSuccessfulVerificationsAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.IdentityVerifications.AsNoTracking()
            .Where(x => x.CounterTransactionId == transactionId && x.Result == (int)VerificationResult.Successful)
            .OrderByDescending(x => x.VerifiedAt)
            .Select(x => new IdentityVerificationSummaryDto(x.Id, x.MaskedDocumentNumber, (VerificationResult)x.Result, x.VerifiedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MoneyOrderReadDto>> GetMoneyOrdersAsync(string? branchCode, bool includeAllBranches, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.MoneyOrders.AsNoTracking()
            .Where(x => includeAllBranches || x.BranchCode == branchCode)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new MoneyOrderReadDto(x.Id, x.Amount, x.Sender, x.Recipient, x.Fee, x.BranchCode, x.CounterTransactionId, x.Status, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffReadDto>> GetStaffAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Staff.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new StaffReadDto(x.Id, x.Name, x.Email, x.Role, x.HomeBranchCode, x.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BranchReadDto>> GetBranchesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Branches.AsNoTracking().OrderBy(x => x.Code)
            .Select(x => new BranchReadDto(x.Id, x.Code, x.Name, x.Region, x.OpeningHours, x.Status, x.Counters.Count))
            .ToListAsync(cancellationToken);
    }
}
