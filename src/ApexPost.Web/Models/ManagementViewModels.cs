using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using ApexPost.Domain;

namespace ApexPost.Web.Models;

public sealed class StaffEditViewModel
{
    [BindNever] public string? Id { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, EnumDataType(typeof(StaffRole))] public StaffRole Role { get; set; }
    [Required, StringLength(12)] public string HomeBranchCode { get; set; } = "";
    [StringLength(128, MinimumLength = 12)] public string? InitialPassword { get; set; }
}
public sealed class BranchEditViewModel
{
    [BindNever] public string? Id { get; set; }
    [Required, StringLength(12, MinimumLength = 3)] public string Code { get; set; } = "";
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, StringLength(80)] public string Region { get; set; } = "";
    [Required, StringLength(40)] public string OpeningHours { get; set; } = "09:00-17:00";
    [Required, StringLength(40, MinimumLength = 2)] public string CounterName { get; set; } = "Counter 1";
}
public sealed class StockAdjustmentViewModel
{
    [Required, StringLength(32, MinimumLength = 3)] public string Sku { get; set; } = "";
    [Range(-10000, 10000)] public int Delta { get; set; }
    [Required, StringLength(250, MinimumLength = 5)] public string Reason { get; set; } = "";
}
