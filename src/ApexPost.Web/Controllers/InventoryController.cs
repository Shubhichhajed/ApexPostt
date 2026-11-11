using ApexPost.Web.Middleware;
using ApexPost.Application;
using ApexPost.Domain;
using ApexPost.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize(Policy = "BranchManagerOnly"), RestrictedEndpoint]
public sealed class InventoryController(IStockRepository stock, IStaffRepository staff, DomainNotifications notifications, LowStockRule lowStockRule, IStockAdjustmentService stockAdjustmentService) : Controller
{
    [HttpGet] public IActionResult Adjust() => View(new StockAdjustmentViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Adjust(StockAdjustmentViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || model.Delta == 0)
        {
            if (model.Delta == 0) ModelState.AddModelError(nameof(model.Delta), "Adjustment must not be zero.");
            return View(model);
        }
        var branch = User.FindFirst("BranchCode")?.Value;
        var staffId = User.FindFirst("StaffId")?.Value;
        if (string.IsNullOrWhiteSpace(branch) || string.IsNullOrWhiteSpace(staffId)) return Forbid();
        var actor = await staff.GetByIdAsync(staffId, cancellationToken);
        if (actor is null || actor.Status != StaffStatus.Active || actor.Role == StaffRole.CounterClerk || actor.Role == StaffRole.Auditor) return Forbid();
        if (actor.Role != StaffRole.Administrator && !string.Equals(actor.HomeBranchCode, branch, StringComparison.OrdinalIgnoreCase)) return Forbid();
        var item = await stock.GetByBranchAndSkuAsync(branch, model.Sku.Trim(), cancellationToken);
        if (item is null)
        {
            ModelState.AddModelError(nameof(model.Sku), "Stock record was not found for this branch.");
            return View(model);
        }
        try
        {
            await stockAdjustmentService.AdjustAsync(branch, item.Sku, model.Delta, model.Reason.Trim(), staffId, cancellationToken);
            var updated = await stock.GetByBranchAndSkuAsync(branch, item.Sku, cancellationToken);
            if (updated is not null && lowStockRule.IsLow(updated)) notifications.RaiseStockLow(updated);
            TempData["Notice"] = "Stock adjustment saved.";
            return RedirectToAction("Index", "Products");
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(model);
        }
    }
}
