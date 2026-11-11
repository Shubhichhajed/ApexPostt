using System.ComponentModel.DataAnnotations;

namespace ApexPost.Web.Models;

public sealed class OfflineSyncRequest
{
    [Required, StringLength(80, MinimumLength = 8)] public string OperationId { get; set; } = string.Empty;
    [Required, StringLength(80)] public string AggregateType { get; set; } = string.Empty;
    [Required, StringLength(32)] public string AggregateId { get; set; } = string.Empty;
    [Required, StringLength(32768, MinimumLength = 2)] public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset ClientOccurredAtUtc { get; set; }
}

public sealed class PriceRevisionViewModel
{
    [EnumDataType(typeof(ApexPost.Domain.ProductCategory))] public ApexPost.Domain.ProductCategory Category { get; set; }
    [Range(typeof(decimal), "0.01", "5.00")] public decimal Multiplier { get; set; } = 1.05m;
}
