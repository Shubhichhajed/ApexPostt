using ApexPost.Application;
using ApexPost.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize]
public sealed class DashboardController(IReportService reports, LowStockRule lowStockRule) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var branch = User.IsInRole("Administrator") || User.IsInRole("Auditor") ? null : User.FindFirst("BranchCode")?.Value;
        var summary = await reports.GetDashboardAsync(branch, cancellationToken);
        ViewBag.LowStockThreshold = lowStockRule.Threshold;
        return View(summary);
    }
}
