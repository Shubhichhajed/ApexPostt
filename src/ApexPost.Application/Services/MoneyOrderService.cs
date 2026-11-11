using ApexPost.Domain;

namespace ApexPost.Application;

public sealed class MoneyOrderService(
    IRepository<MoneyOrder> orders,
    IRepository<IdentityVerification> verifications,
    IRepository<Branch> branches,
    IRepository<Staff> staff,
    IRepository<CounterTransaction> transactions,
    ICounterService counter,
    MoneyOrderPolicy policy, IUnitOfWork unitOfWork) : IMoneyOrderService
{
    public async Task<MoneyOrder> IssueAsync(IssueMoneyOrderCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Amount <= 0m || command.Amount > 1_000_000m)
        {
            throw new DomainException("Money-order amount must be between 0.01 and 1,000,000.00.");
        }
        if (!Enum.IsDefined(typeof(PaymentMethod), command.PaymentMethod))
        {
            throw new DomainException("Unsupported payment method.");
        }
        if (string.IsNullOrWhiteSpace(command.Sender) || string.IsNullOrWhiteSpace(command.Recipient))
        {
            throw new DomainException("Sender and recipient are required.");
        }
        var employee = await staff.GetByIdAsync(command.StaffId, cancellationToken) ?? throw new KeyNotFoundException("Staff member was not found.");
        if (employee.Status != StaffStatus.Active)
        {
            throw new DomainException("Inactive staff cannot issue money orders.");
        }

        CounterTransaction? existingTransaction = null;
        if (!string.IsNullOrWhiteSpace(command.ExistingTransactionId))
        {
            existingTransaction = await transactions.GetByIdAsync(command.ExistingTransactionId, cancellationToken) ?? throw new KeyNotFoundException("Counter transaction was not found.");
            if (existingTransaction.Status != TransactionStatus.Open)
            {
                throw new DomainException("A money order can only be added to an open counter transaction.");
            }
            if (!existingTransaction.BranchCode.Equals(command.BranchCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainException("Money order and counter transaction must belong to the same branch.");
            }
        }

        var branchCode = existingTransaction?.BranchCode ?? command.BranchCode;
        var branch = (await branches.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.Code.Equals(branchCode, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException("Branch was not found.");
        BusinessRules.EnsureCanTransact(employee, branch, branchCode);
        if (policy.RequiresIdentityVerification(command.Amount) && existingTransaction is null)
            throw new DomainException("For a high-value money order, first create an open counter transaction and record identity verification against it.");

        IdentityVerification? verification = null;
        if (policy.RequiresIdentityVerification(command.Amount))
        {
            verification = string.IsNullOrWhiteSpace(command.IdentityVerificationId)
                ? null
                : await verifications.GetByIdAsync(command.IdentityVerificationId, cancellationToken);
            BusinessRules.EnsureMoneyOrderVerified(command.Amount, policy.Threshold,
                verification is not null && verification.Result == VerificationResult.Successful
                && verification.CounterTransactionId == existingTransaction!.Id
                && verification.BranchCode.Equals(branchCode, StringComparison.OrdinalIgnoreCase));
        }

        CounterTransaction transaction;
        var createsAndPaysImmediately = existingTransaction is null;
        if (existingTransaction is not null)
        {
            transaction = existingTransaction;
        }
        else
        {
            var counterId = branch.Counters.FirstOrDefault(x => x.IsActive)?.Id;
            if (string.IsNullOrWhiteSpace(counterId))
            {
                throw new DomainException("No active counter is configured for this branch.");
            }
            // Stage the transaction in the same scoped DbContext; do not commit a
            // partially-created empty basket before its money order and payment.
            transaction = new CounterTransaction { BranchCode = branchCode, CounterId = counterId, ClerkId = command.StaffId };
            await transactions.AddAsync(transaction, cancellationToken);
        }

        var fee = decimal.Round(Math.Max(10m, command.Amount * 0.01m), 2, MidpointRounding.AwayFromZero);
        var order = new MoneyOrder { Amount = command.Amount, Sender = command.Sender.Trim(), Recipient = command.Recipient.Trim(), Fee = fee, BranchCode = branchCode, CounterTransactionId = transaction.Id, Status = MoneyOrderStatus.PendingPayment, IdentityVerificationId = verification?.Id };
        await counter.AddMoneyOrderLineAsync(transaction.Id, command.StaffId, order, cancellationToken);
        await orders.AddAsync(order, cancellationToken);
        if (createsAndPaysImmediately)
        {
            // CompleteAsync commits transaction + lines + money order + payment +
            // stock changes in one SaveChangesAsync database transaction.
            await counter.CompleteAsync(transaction.Id, command.StaffId, command.PaymentMethod, cancellationToken);
        }
        else
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return order;
    }
}
