using ApexPost.Domain;

namespace ApexPost.Application;

public sealed class IdentityVerificationService(
    IRepository<IdentityVerification> verifications,
    IRepository<CounterTransaction> transactions,
    IRepository<Staff> staff,
    IRepository<Branch> branches, IUnitOfWork unitOfWork) : IIdentityVerificationService
{
    public async Task<IdentityVerification> RecordAsync(RecordIdentityVerificationCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.DocumentNumber)) throw new DomainException("A document number is required for verification.");
        if (!Enum.IsDefined(typeof(VerificationResult), command.Result) || command.Result == VerificationResult.Pending) throw new DomainException("A verification result must be Successful or Failed.");
        if (!Enum.IsDefined(typeof(IdentityDocumentType), command.DocumentType)) throw new DomainException("Unsupported identity document type.");
        var transaction = await transactions.GetByIdAsync(command.TransactionId, cancellationToken) ?? throw new KeyNotFoundException("Counter transaction was not found.");
        if (transaction.Status != TransactionStatus.Open) throw new DomainException("Identity verification must be recorded against an open counter transaction.");
        var employee = await staff.GetByIdAsync(command.StaffId, cancellationToken) ?? throw new KeyNotFoundException("Staff member was not found.");
        var branch = (await branches.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.Code.Equals(transaction.BranchCode, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException("Branch was not found.");
        BusinessRules.EnsureCanTransact(employee, branch, transaction.BranchCode);
        var verification = new IdentityVerification { BranchCode = transaction.BranchCode, CounterTransactionId = transaction.Id, DocumentType = command.DocumentType, MaskedDocumentNumber = IdentityVerification.Mask(command.DocumentNumber), Result = command.Result, VerifiedByStaffId = command.StaffId };
        await verifications.AddAsync(verification, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return verification;
    }
}
