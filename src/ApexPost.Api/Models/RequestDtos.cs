using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace ApexPost.Api.Models;

/// <summary>Request payload for creating a shipment.</summary>
public sealed class CreateShipmentRequest
{
    /// <summary>Tracking number in APX-XXXXXXXX format.</summary>
    [Required]
    [RegularExpression(@"^APX-\d{8}$", ErrorMessage = "Tracking number must match format APX-XXXXXXXX")]
    public string TrackingNumber { get; set; } = string.Empty;

    /// <summary>Shipment weight in kilograms, between 0.1 and 70.</summary>
    [Range(typeof(decimal), "0.1", "70", ErrorMessage = "Weight must be between 0.1 and 70 kg")]
    public decimal Weight { get; set; }

    /// <summary>Recipient full name.</summary>
    [Required, StringLength(255, MinimumLength = 1)]
    public string RecipientName { get; set; } = string.Empty;

    /// <summary>Recipient contact phone number.</summary>
    [Required, InternationalPhone]
    public string RecipientPhone { get; set; } = string.Empty;

    /// <summary>Recipient delivery address.</summary>
    [Required, StringLength(500, MinimumLength = 5)]
    public string RecipientAddress { get; set; } = string.Empty;

    /// <summary>Recipient city.</summary>
    [Required, StringLength(100, MinimumLength = 1)]
    public string RecipientCity { get; set; } = string.Empty;

    /// <summary>Optional shipment notes.</summary>
    [StringLength(1000)]
    public string? Notes { get; set; }
}

/// <summary>Request payload that replaces all editable shipment details.</summary>
public sealed class UpdateShipmentRequest
{
    /// <summary>Recipient full name.</summary>
    [Required, StringLength(255, MinimumLength = 1)]
    public string RecipientName { get; set; } = string.Empty;

    /// <summary>Recipient contact phone number.</summary>
    [Required, InternationalPhone]
    public string RecipientPhone { get; set; } = string.Empty;

    /// <summary>Recipient delivery address.</summary>
    [Required, StringLength(500, MinimumLength = 5)]
    public string RecipientAddress { get; set; } = string.Empty;

    /// <summary>Recipient city.</summary>
    [Required, StringLength(100, MinimumLength = 1)]
    public string RecipientCity { get; set; } = string.Empty;

    /// <summary>Shipment weight in kilograms, between 0.1 and 70.</summary>
    [Range(typeof(decimal), "0.1", "70")]
    public decimal Weight { get; set; }

    /// <summary>Optional shipment notes.</summary>
    [StringLength(1000)]
    public string? Notes { get; set; }
}

/// <summary>Request payload for changing shipment status.</summary>
public sealed class UpdateStatusRequest
{
    /// <summary>Next shipment status in the allowed transition sequence.</summary>
    [Required]
    [RegularExpression("^(PickedUp|InTransit|OutForDelivery|Delivered|Failed)$", ErrorMessage = "Unsupported shipment status.")]
    public string Status { get; set; } = string.Empty;
}

/// <summary>Batch of offline scans uploaded by a courier.</summary>
public sealed class ScanUploadRequest : IValidatableObject
{
    /// <summary>One to one hundred offline scans.</summary>
    [Required, MinLength(1), MaxLength(100)]
    public List<ScanItemRequest> Scans { get; set; } = new();

    /// <summary>Validates collection constraints, nested scan payloads and duplicate IDs.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Scans is null) yield break;
        if (Scans.Any(x => x is null))
        {
            yield return new ValidationResult("Scan entries cannot be null", new[] { nameof(Scans) });
            yield break;
        }

        for (var index = 0; index < Scans.Count; index++)
        {
            var scan = Scans[index];
            var nestedResults = new List<ValidationResult>();
            Validator.TryValidateObject(scan, new ValidationContext(scan), nestedResults, validateAllProperties: true);
            foreach (var result in nestedResults)
                yield return new ValidationResult(result.ErrorMessage,
                    result.MemberNames.Select(name => $"{nameof(Scans)}[{index}].{name}"));
        }

        // Repeated IDs are intentionally not rejected here. The persistence layer handles them
        // idempotently so the API can report duplicatesSkipped in its response counts.
    }
}

/// <summary>A single scan recorded by the offline courier store.</summary>
public sealed class ScanItemRequest : IValidatableObject
{
    /// <summary>Stable client-generated identifier in scan::{Guid} format.</summary>
    [Required]
    [RegularExpression(@"^scan::[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        ErrorMessage = "ScanId must use scan::{Guid} format.")]
    public string ScanId { get; set; } = string.Empty;

    /// <summary>Scan type: Pickup, InTransit, Delivered or Failed.</summary>
    [Required]
    [RegularExpression("^(Pickup|InTransit|Delivered|Failed)$", ErrorMessage = "Unsupported scan type.")]
    public string ScanType { get; set; } = string.Empty;

    /// <summary>Courier identifier from the authenticated courier identity.</summary>
    [Required, StringLength(100, MinimumLength = 1)]
    public string CourierId { get; set; } = string.Empty;

    /// <summary>Human-readable location where the scan occurred.</summary>
    [Required, StringLength(250, MinimumLength = 1)]
    public string Location { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the scan occurred.</summary>
    public DateTime ScannedAtUtc { get; set; }

    /// <summary>Optional notes; required for Delivered scans as recipient signature/name.</summary>
    [StringLength(1000)]
    public string? Notes { get; set; }

    /// <summary>Rejects missing/default and future scan times and requires a signature for delivery.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ScannedAtUtc == default)
            yield return new ValidationResult("ScannedAtUtc is required.", new[] { nameof(ScannedAtUtc) });
        else
        {
            var utc = ScannedAtUtc.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(ScannedAtUtc, DateTimeKind.Utc)
                : ScannedAtUtc.ToUniversalTime();
            if (utc > DateTime.UtcNow.AddSeconds(5))
                yield return new ValidationResult("A scan time cannot be in the future.", new[] { nameof(ScannedAtUtc) });
        }

        if (string.Equals(ScanType, "Delivered", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(Notes))
            yield return new ValidationResult("A Delivered scan requires the recipient signature/name in Notes.", new[] { nameof(Notes) });
    }
}

/// <summary>Counts and reasons returned by an idempotent scan upload.</summary>
public sealed class ScanUploadResponse
{
    /// <summary>Number of newly accepted scans.</summary>
    public int Accepted { get; set; }
    /// <summary>Number of previously accepted scan IDs skipped as duplicates.</summary>
    public int DuplicatesSkipped { get; set; }
    /// <summary>Number of rejected scans.</summary>
    public int Rejected { get; set; }
    /// <summary>Reasons for rejected scans.</summary>
    public List<string> RejectionReasons { get; set; } = new();
}

/// <summary>Validates 7-15 digit phone numbers with optional international prefix and common separators.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class InternationalPhoneAttribute : ValidationAttribute
{
    private static readonly Regex Pattern = new(@"^\+?[0-9](?:[0-9 ().-]*[0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public override bool IsValid(object? value)
    {
        if (value is null) return true; // Required handles null/empty separately.
        if (value is not string phone) return false;
        var trimmed = phone.Trim();
        if (trimmed.Length == 0 || !Pattern.IsMatch(trimmed)) return false;
        var digits = trimmed.Count(char.IsDigit);
        return digits is >= 7 and <= 15;
    }

    public override string FormatErrorMessage(string name)
        => $"{name} must contain 7 to 15 digits and may include a leading +, spaces, parentheses, dots or hyphens.";
}
