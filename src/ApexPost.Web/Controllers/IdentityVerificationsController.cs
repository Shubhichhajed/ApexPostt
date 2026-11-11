using ApexPost.Application;
using ApexPost.Domain;
using ApexPost.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize(Roles = "CounterClerk,BranchManager,Administrator")]
public sealed class IdentityVerificationsController(IPlatformQueryService queries, IIdentityVerificationService verificationService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Create(string transactionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(transactionId)) return BadRequest("A counter transaction ID is required.");
        var transaction = await queries.GetTransactionDetailsAsync(transactionId, cancellationToken);
        if (transaction is null) return NotFound();
        if (!User.IsInRole("Administrator") && transaction.BranchCode != User.FindFirst("BranchCode")?.Value) return Forbid();
        if (transaction.Status != TransactionStatus.Open) return BadRequest("Identity verification must be recorded against an open transaction.");
        return View(new IdentityVerificationViewModel { TransactionId = transaction.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(IdentityVerificationViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        var staffId = User.FindFirst("StaffId")?.Value;
        if (string.IsNullOrWhiteSpace(staffId)) return Forbid();
        try
        {
            await verificationService.RecordAsync(new RecordIdentityVerificationCommand(model.TransactionId, staffId, model.DocumentType, model.DocumentNumber, model.Result), cancellationToken);
            TempData["Notice"] = "Identity verification result recorded. Only a masked document number was stored.";
            return RedirectToAction("Details", "Transactions", new { id = model.TransactionId });
        }
        catch (DomainException ex) { ModelState.AddModelError(string.Empty, ex.Message); return View(model); }
    }
}
