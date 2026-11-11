using Microsoft.AspNetCore.Authorization;

namespace ApexPost.Web.Authorization;

public sealed class SameBranchRequirement : IAuthorizationRequirement { }
public sealed class SameBranchAuthorizationHandler(IHttpContextAccessor accessor) : AuthorizationHandler<SameBranchRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SameBranchRequirement requirement)
    {
        var requestedBranch = accessor.HttpContext?.Request.RouteValues["branchCode"]?.ToString();
        var userBranch = context.User.FindFirst("BranchCode")?.Value;
        if (context.User.IsInRole("Administrator") || string.IsNullOrWhiteSpace(requestedBranch) || string.Equals(requestedBranch, userBranch, StringComparison.OrdinalIgnoreCase)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
