using ApexPost.Application;
using ApexPost.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize(Roles = "BranchManager,Auditor,Administrator")]
public sealed class ReportsController(IReportService reports, LowStockRule lowStockRule) : Controller
{
    [Authorize(Policy = "SameBranchOnly")]
    [HttpGet("Reports/Branch/{branchCode}")]
    public async Task<IActionResult> Branch(string branchCode, CancellationToken cancellationToken) => View("DailySales", await reports.GetDailySalesAsync(DateOnly.FromDateTime(DateTime.UtcNow), branchCode, cancellationToken));

    public async Task<IActionResult> DailySales(DateOnly? day, CancellationToken cancellationToken) => View(await reports.GetDailySalesAsync(day ?? DateOnly.FromDateTime(DateTime.UtcNow), User.IsInRole("Administrator") || User.IsInRole("Auditor") ? null : User.FindFirst("BranchCode")?.Value, cancellationToken));
    public async Task<IActionResult> LowStock(CancellationToken cancellationToken) { ViewBag.LowStockThreshold = lowStockRule.Threshold; return View(await reports.GetLowStockAsync(User.IsInRole("Administrator") || User.IsInRole("Auditor") ? null : User.FindFirst("BranchCode")?.Value, cancellationToken)); }
    public async Task<IActionResult> ShippingVolume(CancellationToken cancellationToken) => View(await reports.GetShippingVolumeAsync(User.IsInRole("Administrator") || User.IsInRole("Auditor") ? null : User.FindFirst("BranchCode")?.Value, cancellationToken));
    [Authorize(Roles = "Auditor,Administrator")] public async Task<IActionResult> MoneyOrders(CancellationToken cancellationToken) => View(await reports.GetMoneyOrderAuditAsync(cancellationToken));
}
