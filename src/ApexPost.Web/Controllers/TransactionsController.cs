using ApexPost.Application;
using ApexPost.Domain;
using ApexPost.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize(Roles = "CounterClerk,BranchManager,Administrator")]
public sealed class TransactionsController(ICounterService counter, IBranchRepository branches, IPlatformQueryService queries) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var branch = User.FindFirst("BranchCode")?.Value;
        return View(await queries.GetTransactionsAsync(branch, User.IsInRole("Administrator"), cancellationToken));
    }

    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var branchCode = User.FindFirst("BranchCode")?.Value;
        if (string.IsNullOrWhiteSpace(branchCode)) return Forbid();
        var branch = await branches.GetByCodeAsync(branchCode, cancellationToken);
        if (branch is null) return NotFound();
        var staffId = User.FindFirst("StaffId")?.Value;
        if (string.IsNullOrWhiteSpace(staffId)) return Forbid();
        try
        {
            var transaction = await counter.CreateAsync(branchCode, branch.Counters.FirstOrDefault(x => x.IsActive)?.Id ?? "", staffId, cancellationToken);
            return RedirectToAction(nameof(Details), new { id = transaction.Id });
        }
        catch (DomainException ex) { return BadRequest(ex.Message); }
    }

    public async Task<IActionResult> Details(string id, CancellationToken cancellationToken)
    {
        var transaction = await queries.GetTransactionDetailsAsync(id, cancellationToken);
        if (transaction is null) return NotFound();
        if (!User.IsInRole("Administrator") && transaction.BranchCode != User.FindFirst("BranchCode")?.Value) return Forbid();
        ViewBag.IdentityVerifications = await queries.GetSuccessfulVerificationsAsync(transaction.Id, cancellationToken);
        return View(transaction);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRetail(AddRetailLineViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return await RedisplayDetailsAsync(model.TransactionId, cancellationToken);
        var staffId = User.FindFirst("StaffId")?.Value;
        if (string.IsNullOrWhiteSpace(staffId)) return Forbid();
        try { await counter.AddRetailLineAsync(model.TransactionId, staffId, model.Sku, model.Quantity, cancellationToken); return RedirectToAction(nameof(Details), new { id = model.TransactionId }); }
        catch (DomainException ex) { ModelState.AddModelError(string.Empty, ex.Message); return await RedisplayDetailsAsync(model.TransactionId, cancellationToken); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(CompleteTransactionViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return await RedisplayDetailsAsync(model.TransactionId, cancellationToken);
        var staffId = User.FindFirst("StaffId")?.Value;
        if (string.IsNullOrWhiteSpace(staffId)) return Forbid();
        try { await counter.CompleteAsync(model.TransactionId, staffId, model.PaymentMethod, cancellationToken); return RedirectToAction(nameof(Details), new { id = model.TransactionId }); }
        catch (DomainException ex) { ModelState.AddModelError(string.Empty, ex.Message); return await RedisplayDetailsAsync(model.TransactionId, cancellationToken); }
    }

    [Authorize(Policy = "BranchManagerOnly"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Refund(VoidTransactionViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return await RedisplayDetailsAsync(model.TransactionId, cancellationToken);
        var staffId = User.FindFirst("StaffId")?.Value;
        if (string.IsNullOrWhiteSpace(staffId)) return Forbid();
        try { await counter.RefundAsync(model.TransactionId, staffId, model.Reason, cancellationToken); return RedirectToAction(nameof(Details), new { id = model.TransactionId }); }
        catch (DomainException ex) { ModelState.AddModelError(string.Empty, ex.Message); return await RedisplayDetailsAsync(model.TransactionId, cancellationToken); }
    }

    [Authorize(Policy = "BranchManagerOnly"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Void(VoidTransactionViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return await RedisplayDetailsAsync(model.TransactionId, cancellationToken);
        var staffId = User.FindFirst("StaffId")?.Value;
        if (string.IsNullOrWhiteSpace(staffId)) return Forbid();
        try { await counter.VoidAsync(model.TransactionId, staffId, model.Reason, cancellationToken); return RedirectToAction(nameof(Details), new { id = model.TransactionId }); }
        catch (DomainException ex) { ModelState.AddModelError(string.Empty, ex.Message); return await RedisplayDetailsAsync(model.TransactionId, cancellationToken); }
    }

    private async Task<IActionResult> RedisplayDetailsAsync(string transactionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(transactionId)) return BadRequest(new Microsoft.AspNetCore.Mvc.ValidationProblemDetails(ModelState));
        var transaction = await queries.GetTransactionDetailsAsync(transactionId, cancellationToken);
        if (transaction is null) return NotFound();
        if (!User.IsInRole("Administrator") && transaction.BranchCode != User.FindFirst("BranchCode")?.Value) return Forbid();
        ViewBag.IdentityVerifications = await queries.GetSuccessfulVerificationsAsync(transaction.Id, cancellationToken);
        return View("Details", transaction);
    }
}
