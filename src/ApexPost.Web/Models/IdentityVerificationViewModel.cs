using System.ComponentModel.DataAnnotations;
using ApexPost.Domain;

namespace ApexPost.Web.Models;

public sealed class IdentityVerificationViewModel
{
    [Required, StringLength(32)] public string TransactionId { get; set; } = "";
    [Required, EnumDataType(typeof(IdentityDocumentType))] public IdentityDocumentType DocumentType { get; set; }
    [Required, StringLength(100, MinimumLength = 4)] public string DocumentNumber { get; set; } = "";
    [Required, EnumDataType(typeof(VerificationResult))] public VerificationResult Result { get; set; } = VerificationResult.Successful;
}
