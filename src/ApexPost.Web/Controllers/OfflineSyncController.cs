using ApexPost.Application;
using ApexPost.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/offline-sync")]
public sealed class OfflineSyncController(IOfflineSyncService syncService) : ControllerBase
{
    [HttpPost("operations")]
    public async Task<IActionResult> Accept(OfflineSyncRequest request, CancellationToken cancellationToken)
    {
        var staffId = User.FindFirst("StaffId")?.Value;
        var terminalId = Request.Headers["X-Terminal-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(staffId) || string.IsNullOrWhiteSpace(terminalId) || terminalId.Length > 64)
            return Unauthorized();
        var command = new OfflineSyncCommand($"{staffId}:{terminalId}", request.OperationId, request.AggregateType,
            request.AggregateId, request.PayloadJson, request.ClientOccurredAtUtc);
        try
        {
            var result = await syncService.AcceptAsync(command, cancellationToken);
            return result.IsDuplicate ? Ok(result) : Accepted(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = "Invalid offline operation", Detail = exception.Message });
        }
    }
}
