using ApexPost.Domain;

namespace ApexPost.Application;

public sealed class CounterService(
    ICounterTransactionRepository transactions,
    IStaffRepository staffRepository,
    IBranchRepository branches,
    IProductRepository products,
    IStockRepository stock,
    ICourierService courier,
    IShipmentRepository shipments,
    IIdentityVerificationRepository verifications,
    IMoneyOrderRepository moneyOrders,
    IPaymentRepository payments,
    MoneyOrderPolicy moneyOrderPolicy,
    DomainNotifications notifications, IShippingPricingStrategy pricingStrategy, LowStockRule lowStockRule, IUnitOfWork unitOfWork) : ICounterService
{
    

    public async Task<CounterTransaction> CreateAsync(string branchCode, string counterId, string clerkId, CancellationToken cancellationToken = default)
    {
        var staff = await staffRepository.GetByIdAsync(clerkId, cancellationToken) ?? throw new KeyNotFoundException("Staff member was not found.");
        var branch = await branches.GetByCodeAsync(branchCode, cancellationToken) ?? throw new KeyNotFoundException("Branch was not found.");
        BusinessRules.EnsureCanTransact(staff, branch, branchCode);
        var invalid = ReflectionValidator.Validate(branch).Concat(ReflectionValidator.Validate(staff)).ToArray();
        if (invalid.Length > 0) throw new DomainException(string.Join(" ", invalid));
        if (!branch.Counters.Any(x => x.Id == counterId && x.IsActive)) throw new DomainException("Counter is unavailable.");
        var transaction = new CounterTransaction { BranchCode = branchCode, CounterId = counterId, ClerkId = clerkId };
        await transactions.AddAsync(transaction, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    public async Task<CounterTransaction> AddRetailLineAsync(string transactionId, string actorStaffId, string sku, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0) throw new DomainException("Quantity must be positive.");
        var transaction = await RequiredTransaction(transactionId, cancellationToken);
        await EnsureTransactionCanProcessAsync(transaction, actorStaffId, cancellationToken);
        if (transaction.Status != TransactionStatus.Open) throw new DomainException("Only open transactions can be processed.");
        var normalizedSku = new Sku(sku).Value;
        var product = await products.GetBySkuAsync(normalizedSku, cancellationToken);
        if (product is null || !product.IsActive) throw new KeyNotFoundException("Product was not found.");
        var stockItem = await stock.GetByBranchAndSkuAsync(transaction.BranchCode, product.Sku, cancellationToken) ?? throw new KeyNotFoundException("Stock record was not found.");
        if (stockItem.Quantity < quantity) throw new DomainException("Insufficient stock.");
        var taxRate = BusinessRules.TaxRate(product.TaxCategory);
        transaction.AddLine(new TransactionLine(Guid.NewGuid().ToString("N"), LineKind.RetailProduct, product.Name, product.Sku, quantity, product.UnitPrice, taxRate, decimal.Round(product.UnitPrice * quantity * (1 + taxRate), 2, MidpointRounding.AwayFromZero)));
        // Stock is checked for immediate feedback but deducted only when the sale is paid.
        // This prevents abandoned/open baskets from permanently consuming inventory.
        await transactions.UpdateAsync(transaction, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return transaction;
    }


    public async Task<CounterTransaction> AddShippingLineAsync(string transactionId, string actorStaffId, Shipment shipment, CancellationToken cancellationToken = default)
    {
        var transaction = await RequiredTransaction(transactionId, cancellationToken);
        await EnsureTransactionCanProcessAsync(transaction, actorStaffId, cancellationToken);
        if (transaction.Status != TransactionStatus.Open) throw new DomainException("Only open transactions can be processed.");
        if (shipment.LengthCm <= 0m || shipment.WidthCm <= 0m || shipment.HeightCm <= 0m) throw new DomainException("Shipment dimensions must be greater than zero.");
        var allRates = await courier.GetRatesAsync(cancellationToken);
        shipment.Price = pricingStrategy.Calculate(shipment.Kind, shipment.WeightValue, shipment.DestinationZone, shipment.ServiceLevel, allRates);
        shipment.BranchCode = transaction.BranchCode;
        await shipments.AddAsync(shipment, cancellationToken);
        transaction.AddLine(new TransactionLine(Guid.NewGuid().ToString("N"), LineKind.Shipping, $"{shipment.Kind} — {shipment.DestinationZone} / {shipment.ServiceLevel}", null, 1, shipment.Price, 0m, shipment.Price));
        await transactions.UpdateAsync(transaction, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    public async Task<CounterTransaction> AddMoneyOrderLineAsync(string transactionId, string actorStaffId, MoneyOrder order, CancellationToken cancellationToken = default)
    {
        var transaction = await RequiredTransaction(transactionId, cancellationToken);
        await EnsureTransactionCanProcessAsync(transaction, actorStaffId, cancellationToken);
        if (transaction.Status != TransactionStatus.Open) throw new DomainException("Only open transactions can be processed.");
        if (!string.Equals(transaction.BranchCode, order.BranchCode, StringComparison.OrdinalIgnoreCase)) throw new DomainException("Money order and counter transaction must belong to the same branch.");
        if (order.Amount <= 0m || order.Fee < 0m) throw new DomainException("Money-order amount and fee are invalid.");
        if (moneyOrderPolicy.RequiresIdentityVerification(order.Amount))
        {
            var verification = string.IsNullOrWhiteSpace(order.IdentityVerificationId) ? null : await verifications.GetByIdAsync(order.IdentityVerificationId, cancellationToken);
            BusinessRules.EnsureMoneyOrderVerified(order.Amount, moneyOrderPolicy.Threshold, verification is not null && verification.Result == VerificationResult.Successful && verification.CounterTransactionId == transaction.Id && verification.BranchCode.Equals(transaction.BranchCode, StringComparison.OrdinalIgnoreCase));
        }
        transaction.AddLine(new TransactionLine(Guid.NewGuid().ToString("N"), LineKind.MoneyOrder, $"Money order to {order.Recipient}", null, 1, order.Amount + order.Fee, 0m, order.Amount + order.Fee));
        await transactions.UpdateAsync(transaction, cancellationToken);
        // This method stages the money-order line only. MoneyOrderService owns the
        // use-case commit so the order, line, transaction and payment are atomic.
        return transaction;
    }

    public async Task<CounterTransaction> CompleteAsync(string transactionId, string actorStaffId, PaymentMethod method, CancellationToken cancellationToken = default)
    {
        var transaction = await RequiredTransaction(transactionId, cancellationToken);
        await EnsureTransactionCanProcessAsync(transaction, actorStaffId, cancellationToken);
        var retailQuantities = transaction.Lines.Where(x => x.Kind == LineKind.RetailProduct && !string.IsNullOrWhiteSpace(x.Sku))
            .GroupBy(x => x.Sku!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Sku = group.Key, Quantity = group.Sum(line => line.Quantity) })
            .OrderBy(group => group.Sku, StringComparer.Ordinal).ToArray();
        var stockItems = await stock.GetByBranchAsync(transaction.BranchCode, cancellationToken);
        foreach (var required in retailQuantities)
        {
            var stockItem = stockItems.FirstOrDefault(x => x.BranchCode == transaction.BranchCode && x.Sku == required.Sku)
                ?? throw new DomainException($"Stock record for {required.Sku} was not found.");
            if (stockItem.Quantity < required.Quantity) throw new ConcurrencyConflictException($"Stock for {required.Sku} changed while this basket was open. Available {stockItem.Quantity}, requested {required.Quantity}. Refresh the basket and retry.");
            stockItem.Adjust(-required.Quantity);
            await stock.UpdateAsync(stockItem, cancellationToken);
        }
        transaction.MarkPaid(method);
        await transactions.UpdateAsync(transaction, cancellationToken);
        await payments.AddAsync(new Payment { CounterTransactionId = transaction.Id, Method = method, Amount = transaction.Total }, cancellationToken);
        var issuedOrders = new List<MoneyOrder>();
        foreach (var order in await moneyOrders.GetByTransactionAsync(transaction.Id, cancellationToken))
        {
            if (order.Status != MoneyOrderStatus.PendingPayment) continue;
            order.Status = MoneyOrderStatus.Issued;
            await moneyOrders.UpdateAsync(order, cancellationToken);
            issuedOrders.Add(order);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var order in issuedOrders)
        {
            notifications.RaiseMoneyOrderIssued(order);
            if (order.Amount > moneyOrderPolicy.Threshold) notifications.RaiseHighValueMoneyOrderIssued(order);
        }
        foreach (var required in retailQuantities)
        {
            var stockItem = stockItems.First(x => x.BranchCode == transaction.BranchCode && x.Sku == required.Sku);
            if (lowStockRule.IsLow(stockItem)) notifications.RaiseStockLow(stockItem);
        }
        notifications.RaiseTransactionCompleted(transaction);
        return transaction;
    }

    public async Task<CounterTransaction> VoidAsync(string transactionId, string staffId, string reason, CancellationToken cancellationToken = default) => await ChangePaidTransactionAsync(transactionId, staffId, reason, false, cancellationToken);
    public async Task<CounterTransaction> RefundAsync(string transactionId, string staffId, string reason, CancellationToken cancellationToken = default) => await ChangePaidTransactionAsync(transactionId, staffId, reason, true, cancellationToken);

    private async Task<CounterTransaction> ChangePaidTransactionAsync(string transactionId, string staffId, string reason, bool refund, CancellationToken cancellationToken)
    {
        var staff = await staffRepository.GetByIdAsync(staffId, cancellationToken) ?? throw new KeyNotFoundException("Staff member was not found.");
        var transaction = await RequiredTransaction(transactionId, cancellationToken);
        if (staff.Status != StaffStatus.Active) throw new DomainException("Inactive staff cannot process transactions.");
        if (staff.Role != StaffRole.Administrator && !string.Equals(staff.HomeBranchCode, transaction.BranchCode, StringComparison.OrdinalIgnoreCase)) throw new DomainException("Staff can only process transactions for their assigned branch.");
        BusinessRules.EnsureManager(staff, reason);
        if (refund) transaction.Refund(reason); else transaction.Void(reason);
        // Paid retail inventory is returned exactly once because only Paid transactions can enter this path.
        var stockItems = await stock.GetByBranchAsync(transaction.BranchCode, cancellationToken);
        var returnedQuantities = transaction.Lines.Where(x => x.Kind == LineKind.RetailProduct && !string.IsNullOrWhiteSpace(x.Sku))
            .GroupBy(x => x.Sku!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Sku = group.Key, Quantity = group.Sum(line => line.Quantity) })
            .OrderBy(group => group.Sku, StringComparer.Ordinal);
        foreach (var returned in returnedQuantities)
        {
            var stockItem = stockItems.FirstOrDefault(x => x.BranchCode == transaction.BranchCode && x.Sku == returned.Sku)
                ?? throw new DomainException($"Stock record for {returned.Sku} was not found.");
            stockItem.Adjust(returned.Quantity);
            await stock.UpdateAsync(stockItem, cancellationToken);
        }
        await transactions.UpdateAsync(transaction, cancellationToken);
        foreach (var order in await moneyOrders.GetByTransactionAsync(transaction.Id, cancellationToken))
        {
            if (order.Status == MoneyOrderStatus.Cancelled) continue;
            order.Status = MoneyOrderStatus.Cancelled;
            await moneyOrders.UpdateAsync(order, cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var stockItem in stockItems.Where(x => x.BranchCode == transaction.BranchCode && transaction.Lines.Any(line => line.Sku == x.Sku)))
            if (lowStockRule.IsLow(stockItem)) notifications.RaiseStockLow(stockItem);
        if (refund) notifications.RaiseTransactionRefunded(transaction); else notifications.RaiseTransactionVoided(transaction);
        return transaction;
    }

    private async Task EnsureTransactionCanProcessAsync(CounterTransaction transaction, string actorStaffId, CancellationToken token)
    {
        var actor = await staffRepository.GetByIdAsync(actorStaffId, token) ?? throw new KeyNotFoundException("Staff member was not found.");
        var branch = await branches.GetByCodeAsync(transaction.BranchCode, token) ?? throw new KeyNotFoundException("Branch was not found.");
        BusinessRules.EnsureCanTransact(actor, branch, transaction.BranchCode);
    }

    private async Task<CounterTransaction> RequiredTransaction(string id, CancellationToken token) => await transactions.GetByIdAsync(id, token) ?? throw new KeyNotFoundException("Transaction was not found.");
}
