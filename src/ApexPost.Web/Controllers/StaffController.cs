using ApexPost.Web.Middleware;
using ApexPost.Application;
using ApexPost.Domain;
using ApexPost.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize(Roles = "Administrator"), RestrictedEndpoint]
public sealed class StaffController(IStaffRepository staff, IBranchRepository branches, IPasswordService passwords, IUnitOfWork unitOfWork, IPlatformQueryService queries) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken) => View(await queries.GetStaffAsync(cancellationToken));
    [HttpGet] public IActionResult Create() => View("Edit", new StaffEditViewModel());
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StaffEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View("Edit", model);
        if (string.IsNullOrWhiteSpace(model.InitialPassword) || model.InitialPassword.Length < 12) { ModelState.AddModelError(nameof(model.InitialPassword), "An initial password of at least 12 characters is required."); return View("Edit", model); }
        if ((await staff.GetAllAsync(cancellationToken)).Any(x => string.Equals(x.Email, model.Email, StringComparison.OrdinalIgnoreCase))) { ModelState.AddModelError(nameof(model.Email), "Email is already registered."); return View("Edit", model); }
        if (!(await branches.GetAllAsync(cancellationToken)).Any(x => x.Code == model.HomeBranchCode)) { ModelState.AddModelError(nameof(model.HomeBranchCode), "Branch does not exist."); return View("Edit", model); }
        await staff.AddAsync(new Staff { Name = model.Name.Trim(), Email = model.Email.Trim(), Role = model.Role, HomeBranchCode = model.HomeBranchCode.Trim().ToUpperInvariant(), PasswordHash = passwords.Hash(model.InitialPassword!) }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }
    [HttpGet] public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        var item = await staff.GetByIdAsync(id, cancellationToken); if (item is null) return NotFound();
        return View(new StaffEditViewModel { Id = item.Id, Name = item.Name, Email = item.Email, Role = item.Role, HomeBranchCode = item.HomeBranchCode });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, StaffEditViewModel model, CancellationToken cancellationToken)
    {
        model.Id = id;
        if (!ModelState.IsValid) return View(model);
        var item = await staff.GetByIdAsync(id, cancellationToken); if (item is null) return NotFound();
        if ((await staff.GetAllAsync(cancellationToken)).Any(x => x.Id != item.Id && string.Equals(x.Email, model.Email, StringComparison.OrdinalIgnoreCase))) { ModelState.AddModelError(nameof(model.Email), "Email is already registered."); return View(model); }
        if (!(await branches.GetAllAsync(cancellationToken)).Any(x => x.Code.Equals(model.HomeBranchCode, StringComparison.OrdinalIgnoreCase))) { ModelState.AddModelError(nameof(model.HomeBranchCode), "Branch does not exist."); return View(model); }
        item.Name = model.Name.Trim(); item.Email = model.Email.Trim(); item.Role = model.Role; item.HomeBranchCode = model.HomeBranchCode.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(model.InitialPassword)) item.PasswordHash = passwords.Hash(model.InitialPassword);
        await staff.UpdateAsync(item, cancellationToken); await unitOfWork.SaveChangesAsync(cancellationToken); return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Deactivate(string id, CancellationToken cancellationToken)
    {
        var item = await staff.GetByIdAsync(id, cancellationToken); if (item is null) return NotFound();
        item.Status = StaffStatus.Inactive; await staff.UpdateAsync(item, cancellationToken); await unitOfWork.SaveChangesAsync(cancellationToken); return RedirectToAction(nameof(Index));
    }
}
