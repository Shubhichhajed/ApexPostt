using System.Security.Claims;
using ApexPost.Application;
using ApexPost.Domain;
using ApexPost.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ApexPost.Web.Controllers;

public sealed class AccountController(IStaffRepository staffRepository, IPasswordService passwords, ILogger<AccountController> logger) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        var staff = await staffRepository.GetByEmailAsync(model.Email.Trim(), cancellationToken);
        if (staff is null || staff.Status != StaffStatus.Active || staff.PasswordHash == "UNCONFIGURED" || !passwords.Verify(staff.PasswordHash, model.Password))
        {
            logger.LogWarning("Failed login attempt from {RemoteIp}", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            ModelState.AddModelError(string.Empty, "The email or password is invalid.");
            return View(model);
        }
        var claims = new List<Claim>
        {
            new("StaffId", staff.Id), new(ClaimTypes.NameIdentifier, staff.Id), new(ClaimTypes.Name, staff.Name),
            new(ClaimTypes.Email, staff.Email), new(ClaimTypes.Role, staff.Role.ToString()), new("BranchCode", staff.HomeBranchCode)
        };
        var identity = new ClaimsIdentity(claims, "ApexPostCookie");
        await HttpContext.SignInAsync("ApexPostCookie", new ClaimsPrincipal(identity), new Microsoft.AspNetCore.Authentication.AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl)) return Redirect(model.ReturnUrl);
        return RedirectToAction("Index", "Dashboard");
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("ApexPostCookie");
        return RedirectToAction(nameof(Login));
    }
    [AllowAnonymous, HttpGet] public IActionResult AccessDenied() => View();
}
