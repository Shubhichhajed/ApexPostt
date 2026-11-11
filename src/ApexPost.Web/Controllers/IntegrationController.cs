using ApexPost.Domain;
using ApexPost.Web.Middleware;
using ApexPost.Application;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ApexPost.Web.Controllers;

[ApiController, Route("integration/parcel-status")]
public sealed class IntegrationController(ICourierService courier) : ControllerBase
{
    [HttpGet("{id}")]
    [RequireApiKey, RestrictedEndpoint, EnableRateLimiting("partner"), EnableCors("PartnerWidget")]
    public async Task<IActionResult> GetStatus(string id, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var status = await courier.GetTrackingStatusAsync(id, timeout.Token);
            return status is null ? NotFound() : Ok(new { ShipmentId = id, Status = status, CorrelationId = HttpContext.TraceIdentifier });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return StatusCode(StatusCodes.Status504GatewayTimeout, new { title = "Courier tracking lookup timed out.", traceId = HttpContext.TraceIdentifier }); }
    }
}
