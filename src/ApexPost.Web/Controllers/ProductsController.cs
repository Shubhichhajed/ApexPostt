using ApexPost.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApexPost.Web.Models;
using ApexPost.Domain;

namespace ApexPost.Web.Controllers;

[Authorize]
public sealed class ProductsController(IPlatformQueryService queries, LowStockRule lowStockRule, IProductPriceRevisionService priceRevisionService) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewBag.LowStockThreshold = lowStockRule.Threshold;
        var branch = User.FindFirst("BranchCode")?.Value;
        var includeAll = User.IsInRole("Auditor") || User.IsInRole("Administrator");
        return View(await queries.GetProductStockAsync(branch, includeAll, cancellationToken));
    }

    [Authorize(Roles = "Administrator"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RevisePrices(PriceRevisionViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.LowStockThreshold = lowStockRule.Threshold;
            var branch = User.FindFirst("BranchCode")?.Value;
            return View(nameof(Index), await queries.GetProductStockAsync(branch, true, cancellationToken));
        }
        var affected = await priceRevisionService.ReviseCategoryPricesAsync(model.Category, model.Multiplier, cancellationToken);
        TempData["Notice"] = $"Price revision applied to {affected} products.";
        return RedirectToAction(nameof(Index));
    }
}
