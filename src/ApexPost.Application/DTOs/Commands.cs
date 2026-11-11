using ApexPost.Domain;

namespace ApexPost.Application;

public sealed record IssueMoneyOrderCommand(decimal Amount, string Sender, string Recipient, string BranchCode, string StaffId, string? ExistingTransactionId, string? IdentityVerificationId, PaymentMethod PaymentMethod);
public sealed record RecordIdentityVerificationCommand(string TransactionId, string StaffId, IdentityDocumentType DocumentType, string DocumentNumber, VerificationResult Result);
