using Asp.Versioning;
using ApexPost.Api.Models;
using ApexPost.Api.Security;
using ApexPost.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Controllers;

/// <summary>
/// Read-only partner lookup. JWT is not used here (AllowAnonymous); the X-Api-Key header is checked
/// by <see cref="PartnerApiKeyMiddleware"/> because of the <see cref="RequirePartnerApiKeyAttribute"/> marker.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePartnerApiKey]
[Route("api/v{version:apiVersion}/partner/shipments")]
[ApiVersion("2.0")]
[Produces("application/json")]
public sealed class PartnerShipmentsController(IShipmentService shipments) : ControllerBase
{
    /// <summary>Returns tracking details for an approved partner. Requires the X-Api-Key header.</summary>
    [HttpGet("{trackingNumber:trackingNumber}", Name = "GetPartnerShipment")]
    [ProducesResponseType(typeof(ShipmentResponseV2), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [EndpointSummary("Get shipment details for an approved partner")]
    [Tags("Partner")]
    public async Task<IActionResult> GetShipment(string trackingNumber, CancellationToken cancellationToken)
    {
        var shipment = await shipments.GetShipmentAsync(trackingNumber, cancellationToken);
        return shipment is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Shipment not found",
                detail: $"No shipment exists with tracking number {trackingNumber}.")
            : Ok(ShipmentResponseMapper.ToV2(shipment));
    }
}
