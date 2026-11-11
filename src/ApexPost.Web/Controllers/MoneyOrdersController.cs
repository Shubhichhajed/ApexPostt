using ApexPost.Application;
using ApexPost.Domain;
using ApexPost.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize(Roles = "CounterClerk,BranchManager,Administrator")]
public sealed class MoneyOrdersController(IPlatformQueryService queries, IMoneyOrderService moneyOrderService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Create(string? transactionId, CancellationToken cancellationToken)
    {
        var branchCode = User.FindFirst("BranchCode")?.Value ?? "";
        if (!string.IsNullOrWhiteSpace(transactionId))
        {
            var transaction = await queries.GetTransactionDetailsAsync(transactionId, cancellationToken);
            if (transaction is null) return NotFound();
            if (transaction.Status != TransactionStatus.Open) return BadRequest("Money orders can only be added to open transactions.");
            if (!User.IsInRole("Administrator") && !string.Equals(transaction.BranchCode, branchCode, StringComparison.OrdinalIgnoreCase)) return Forbid();
            branchCode = transaction.BranchCode;
        }
        return View(new MoneyOrderViewModel { BranchCode = branchCode, TransactionId = transactionId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MoneyOrderViewModel model, CancellationToken cancellationToken)
    {
        var userBranch = User.FindFirst("BranchCode")?.Value;
        if (!ModelState.IsValid) return View(model);
        if (!string.IsNullOrWhiteSpace(model.TransactionId))
        {
            var transaction = await queries.GetTransactionDetailsAsync(model.TransactionId, cancellationToken);
            if (transaction is null) return NotFound();
            if (transaction.Status != TransactionStatus.Open) { ModelState.AddModelError(string.Empty, "Money orders can only be added to open transactions."); return View(model); }
            if (!User.IsInRole("Administrator") && !string.Equals(transaction.BranchCode, userBranch, StringComparison.OrdinalIgnoreCase)) return Forbid();
            if (!transaction.BranchCode.Equals(model.BranchCode, StringComparison.OrdinalIgnoreCase)) { ModelState.AddModelError(nameof(model.BranchCode), "Money order and transaction branch must match."); return View(model); }
        }
        else if (!User.IsInRole("Administrator") && !string.Equals(userBranch, model.BranchCode, StringComparison.OrdinalIgnoreCase)) return Forbid();
        var staffId = User.FindFirst("StaffId")?.Value;
        if (string.IsNullOrWhiteSpace(staffId)) return Forbid();
        try
        {
            await moneyOrderService.IssueAsync(new IssueMoneyOrderCommand(model.Amount, model.Sender, model.Recipient, model.BranchCode, staffId, model.TransactionId, model.IdentityVerificationId, model.PaymentMethod), cancellationToken);
            return string.IsNullOrWhiteSpace(model.TransactionId) ? RedirectToAction(nameof(Index)) : RedirectToAction("Details", "Transactions", new { id = model.TransactionId });
        }
        catch (DomainException ex) { ModelState.AddModelError(string.Empty, ex.Message); return View(model); }
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var branch = User.FindFirst("BranchCode")?.Value;
        return View(await queries.GetMoneyOrdersAsync(branch, User.IsInRole("Administrator"), cancellationToken));
    }
}
