using Xunit;
using ApexPost.Application;
using ApexPost.Domain;
using ApexPost.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ApexPost.IntegrationTests.Authentication;

namespace ApexPost.IntegrationTests.Persistence;

public sealed class SqlServerPersistenceTests
{
    [Fact]
    public async Task Migrations_and_seed_create_required_relational_data()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
        Assert.True(await db.Database.CanConnectAsync());
        Assert.Equal(5, await db.Branches.CountAsync());
        Assert.Equal(20, await db.Staff.CountAsync());
        Assert.Equal(30, await db.Products.CountAsync());
        Assert.True(await db.CounterTransactions.CountAsync() >= 200);
        Assert.True(await db.Payments.CountAsync() >= 200);
        var samplePayment = await db.Payments.AsNoTracking().FirstAsync();
        var sampleShipment = await db.Shipments.AsNoTracking().FirstAsync();
        Assert.True(samplePayment.AmountValue.Amount > 0m);
        Assert.True(sampleShipment.WeightValue.Kilograms > 0m);
        var seededShipments = await db.Shipments.AsNoTracking().ToListAsync();
        Assert.NotEmpty(seededShipments);
        Assert.All(seededShipments, shipment =>
        {
            Assert.True(shipment.LengthCm > 0m);
            Assert.True(shipment.WidthCm > 0m);
            Assert.True(shipment.HeightCm > 0m);
        });
        Assert.Contains(await db.Branches.AsNoTracking().ToListAsync(), branch => branch.Code == "CHN01" && branch.Status == BranchStatus.Closed);
        Assert.Contains(await db.Staff.AsNoTracking().ToListAsync(), member => member.Status == StaffStatus.Inactive);
    }

    [Fact]
    public async Task Rowversion_rejects_a_stale_stock_update()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApexPostDbContext>>();
        await using var first = await contextFactory.CreateDbContextAsync();
        await using var second = await contextFactory.CreateDbContextAsync();
        var left = await first.BranchStock.FirstAsync();
        var right = await second.BranchStock.SingleAsync(x => x.Id == left.Id);
        left.Adjust(-1);
        right.Adjust(-1);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Unit_of_work_rolls_back_all_changes_when_operation_fails()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApexPostDbContext>>();
        await using var writeContext = await contextFactory.CreateDbContextAsync();
        var unitOfWork = new EfUnitOfWork(writeContext);
        var code = "ROLLBACK" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            writeContext.Branches.Add(new Branch { Id = Guid.NewGuid().ToString("N"), Code = code, Name = "Rollback Test", Region = "Test", Status = BranchStatus.Active });
            await writeContext.SaveChangesAsync(token);
            throw new InvalidOperationException("Force rollback for test.");
        }));
        await using var verifyContext = await contextFactory.CreateDbContextAsync();
        Assert.False(await verifyContext.Branches.AnyAsync(x => x.Code == code));
    }

    [Fact]
    public async Task Failed_sale_rolls_back_stock_transaction_lines_and_payment_together()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
        var stock = await db.BranchStock.SingleAsync(x => x.BranchCode == "IND01" && x.Sku == "APX-001");
        var originalQuantity = stock.Quantity;
        var clerk = await db.Staff.AsNoTracking().FirstAsync(x => x.Email == "counterclerk1@apexpost.local");
        var transactionId = Guid.NewGuid().ToString("N");
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            stock.Adjust(-1);
            var transaction = new CounterTransaction { Id = transactionId, BranchCode = "IND01", CounterId = "IND01-C1", ClerkId = clerk.Id };
            transaction.AddLine(new TransactionLine(Guid.NewGuid().ToString("N"), LineKind.RetailProduct, "Rollback item", "APX-001", 1, 10m, 0.18m, 11.80m));
            transaction.MarkPaid(PaymentMethod.Cash);
            db.CounterTransactions.Add(transaction);
            db.Payments.Add(new Payment { CounterTransactionId = transaction.Id, Method = PaymentMethod.Cash, Amount = transaction.Total });
            await db.SaveChangesAsync(token);
            throw new InvalidOperationException("Simulated failure before transaction commit.");
        }));

        await using var verificationContext = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApexPostDbContext>>().CreateDbContextAsync();
        var persistedStock = await verificationContext.BranchStock.SingleAsync(x => x.BranchCode == "IND01" && x.Sku == "APX-001");
        Assert.Equal(originalQuantity, persistedStock.Quantity);
        Assert.False(await verificationContext.CounterTransactions.AnyAsync(x => x.Id == transactionId));
        Assert.False(await verificationContext.Payments.AnyAsync(x => x.CounterTransactionId == transactionId));
    }

    [Fact]
    public async Task Void_and_refund_restore_stock_and_persist_transaction_state_atomically()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
        var stockItem = await db.BranchStock.SingleAsync(x => x.BranchCode == "IND01" && x.Sku == "APX-001");
        var originalQuantity = stockItem.Quantity;
        var clerk = await db.Staff.SingleAsync(x => x.Role == StaffRole.CounterClerk && x.HomeBranchCode == "IND01" && x.Status == StaffStatus.Active);
        var manager = await db.Staff.SingleAsync(x => x.Role == StaffRole.BranchManager && x.HomeBranchCode == "IND01" && x.Status == StaffStatus.Active);
        var branch = await db.Branches.SingleAsync(x => x.Code == "IND01");
        var counterId = branch.Counters.First(x => x.IsActive).Id;
        var service = scope.ServiceProvider.GetRequiredService<ICounterService>();

        async Task<CounterTransaction> CreatePaidRetailSaleAsync()
        {
            stockItem.Adjust(-1);
            var sale = new CounterTransaction { BranchCode = "IND01", CounterId = counterId, ClerkId = clerk.Id };
            sale.AddLine(new TransactionLine(Guid.NewGuid().ToString("N"), LineKind.RetailProduct, "Test product", "APX-001", 1, 10m, 0.18m, 11.80m));
            sale.MarkPaid(PaymentMethod.Cash);
            db.CounterTransactions.Add(sale);
            db.Payments.Add(new Payment { CounterTransactionId = sale.Id, Method = PaymentMethod.Cash, Amount = sale.Total });
            await db.SaveChangesAsync();
            return sale;
        }

        var voidSale = await CreatePaidRetailSaleAsync();
        await service.VoidAsync(voidSale.Id, manager.Id, "Customer requested cancellation");
        Assert.Equal(originalQuantity, stockItem.Quantity);
        Assert.Equal(TransactionStatus.Voided, voidSale.Status);

        var refundSale = await CreatePaidRetailSaleAsync();
        await service.RefundAsync(refundSale.Id, manager.Id, "Refund approved by branch manager");
        Assert.Equal(originalQuantity, stockItem.Quantity);
        Assert.Equal(TransactionStatus.Refunded, refundSale.Status);
    }

    [Fact]
    public async Task Screen_query_service_returns_projected_read_models()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IPlatformQueryService>();
        var products = await queries.GetProductStockAsync("IND01", false);
        var transactions = await queries.GetTransactionsAsync("IND01", false);
        var branches = await queries.GetBranchesAsync();
        var staff = await queries.GetStaffAsync();
        var orders = await queries.GetMoneyOrdersAsync("IND01", false);

        Assert.Equal(30, products.Count);
        Assert.NotEmpty(transactions);
        Assert.Equal(5, branches.Count);
        Assert.Equal(20, staff.Count);
        Assert.NotEmpty(orders);
        var details = await queries.GetTransactionDetailsAsync(transactions[0].Id);
        Assert.NotNull(details);
        Assert.NotEmpty(details.Lines);
    }

    [Fact]
    public async Task Stored_procedure_reports_are_callable_with_parameterized_ef_api()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
        var sampleTransaction = await db.CounterTransactions.AsNoTracking().FirstAsync(x => x.Status == TransactionStatus.Paid && x.CompletedAt != null);
        var day = DateOnly.FromDateTime(sampleTransaction.CompletedAt!.Value.UtcDateTime);
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = start.AddDays(1);
        var transactionIds = await db.CounterTransactions.AsNoTracking()
            .Where(x => x.BranchCode == sampleTransaction.BranchCode && x.Status == TransactionStatus.Paid && x.CompletedAt >= start && x.CompletedAt < end)
            .Select(x => x.Id).ToListAsync();
        var lines = await db.CounterTransactions.AsNoTracking().Where(x => transactionIds.Contains(x.Id))
            .SelectMany(x => x.Lines).ToListAsync();
        var daily = await reports.GetDailySalesAsync(day, sampleTransaction.BranchCode);
        var dailyRow = Assert.Single(daily);
        Assert.Equal(lines.Where(x => x.Kind == LineKind.RetailProduct).Sum(x => x.LineTotal), dailyRow.RetailSales);
        Assert.Equal(lines.Where(x => x.Kind != LineKind.RetailProduct).Sum(x => x.LineTotal), dailyRow.ServiceSales);

        var lowStock = await reports.GetLowStockAsync(null);
        Assert.Equal(await db.BranchStock.AsNoTracking().CountAsync(x => x.Quantity <= 5), lowStock.Count);
        var shipping = await reports.GetShippingVolumeAsync(null);
        Assert.Equal(await db.Shipments.AsNoTracking().CountAsync(), shipping.Sum(x => x.Count));
        var moneyOrders = await reports.GetMoneyOrderAuditAsync();
        Assert.Equal(await db.MoneyOrders.AsNoTracking().CountAsync(), moneyOrders.Sum(x => x.MoneyOrders));
        Assert.Equal(await db.IdentityVerifications.AsNoTracking().CountAsync(x => x.Result == (int)VerificationResult.Successful), moneyOrders.Sum(x => x.SuccessfulVerifications));
        Assert.Equal(await db.IdentityVerifications.AsNoTracking().CountAsync(x => x.Result == (int)VerificationResult.Failed), moneyOrders.Sum(x => x.FailedVerifications));
    }
    [Fact]
    public async Task Stock_adjustment_procedure_round_trips_branch_and_sku_value_objects()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
        var manager = await db.Staff.AsNoTracking().FirstAsync(x => x.Role == StaffRole.BranchManager && x.HomeBranchCode == "IND01" && x.Status == StaffStatus.Active);
        var service = scope.ServiceProvider.GetRequiredService<IStockAdjustmentService>();
        await service.AdjustAsync("IND01", "APX-001", 1, "Assessment stock receipt", manager.Id);
        var adjustment = await db.StockAdjustments.AsNoTracking().OrderByDescending(x => x.ChangedAtUtc).FirstAsync();
        Assert.Equal("IND01", adjustment.BranchCodeValue.Value);
        Assert.Equal("APX-001", adjustment.SkuValue.Value);
        var createdBy = await db.StockAdjustments.AsNoTracking().Where(x => x.Id == adjustment.Id).SingleAsync();
        Assert.Equal(manager.Id, createdBy.ChangedBy);
    }

    [Fact]
    public async Task Offline_sync_inbox_is_idempotent_for_a_terminal_operation()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var sync = scope.ServiceProvider.GetRequiredService<IOfflineSyncService>();
        var operation = new OfflineSyncCommand("staff-1:terminal-01", "operation-000001", "CounterTransaction", "tx-offline-1", "{\"status\":\"Open\"}", DateTimeOffset.UtcNow);
        var first = await sync.AcceptAsync(operation);
        var second = await sync.AcceptAsync(operation);
        Assert.False(first.IsDuplicate);
        Assert.True(second.IsDuplicate);
    }

    [Fact]
    public async Task Set_based_price_revision_updates_only_the_selected_category()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IProductPriceRevisionService>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApexPostDbContext>>();
        await using var beforeContext = await dbFactory.CreateDbContextAsync();
        var before = await beforeContext.Products.AsNoTracking().Where(x => x.Category == ProductCategory.Packaging).Select(x => x.UnitPrice).ToArrayAsync();
        var changed = await service.ReviseCategoryPricesAsync(ProductCategory.Packaging, 1.10m);
        await using var afterContext = await dbFactory.CreateDbContextAsync();
        var after = await afterContext.Products.AsNoTracking().Where(x => x.Category == ProductCategory.Packaging).Select(x => x.UnitPrice).ToArrayAsync();
        Assert.Equal(before.Length, changed);
        Assert.Equal(before.Length, after.Length);
    }

}
