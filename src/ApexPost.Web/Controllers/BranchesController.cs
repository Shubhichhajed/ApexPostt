using ApexPost.Web.Middleware;
using ApexPost.Application;
using ApexPost.Domain;
using ApexPost.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

/// <summary>Thin controller: binds the view model, maps it to a command and lets the application service commit.</summary>
[Authorize(Roles = "Administrator"), RestrictedEndpoint]
public sealed class BranchesController(IBranchRepository branches, IBranchAdministrationService administration, IPlatformQueryService queries) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken) => View(await queries.GetBranchesAsync(cancellationToken));

    [HttpGet] public IActionResult Create() => View("Edit", new BranchEditViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BranchEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View("Edit", model);
        try
        {
            await administration.CreateAsync(ToCommand(model), cancellationToken);
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(nameof(model.Code), ex.Message);
            return View("Edit", model);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        var item = await branches.GetByIdAsync(id, cancellationToken);
        if (item is null) return NotFound();
        return View(new BranchEditViewModel { Id = item.Id, Code = item.Code, Name = item.Name, Region = item.Region, OpeningHours = item.OpeningHours, CounterName = item.Counters.FirstOrDefault()?.Name ?? "Counter 1" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, BranchEditViewModel model, CancellationToken cancellationToken)
    {
        model.Id = id;
        if (!ModelState.IsValid) return View(model);
        var updated = await administration.UpdateAsync(id, ToCommand(model), cancellationToken);
        return updated is null ? NotFound() : RedirectToAction(nameof(Index));
    }

    private static SaveBranchCommand ToCommand(BranchEditViewModel model) =>
        new(model.Code, model.Name, model.Region, model.OpeningHours, model.CounterName);
}
