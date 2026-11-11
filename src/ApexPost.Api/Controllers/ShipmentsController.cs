using Asp.Versioning;
using ApexPost.Api.Models;
using ApexPost.Api.Security;
using ApexPost.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Controllers;

/// <summary>Version 1 shipment endpoints (deprecated). The flat Part B contract is kept unchanged for existing clients.</summary>
[ApiController]
[Route("api/v{version:apiVersion}/shipments")]
[ApiVersion("1.0", Deprecated = true)]
[Produces("application/json")]
public sealed class ShipmentsController(IShipmentService shipmentService) : ControllerBase
{
    /// <summary>Returns a paged list of shipments, optionally filtered by status and city.</summary>
    /// <remarks>pageSize defaults to 20 and may not exceed 50.</remarks>
    [HttpGet(Name = "ListShipmentsV1")]
    [Authorize]
    [ProducesResponseType(typeof(PaginatedShipmentsResponse<ShipmentResponseV1>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [EndpointSummary("List shipments with paging and optional status/city filters")]
    [Tags("Shipments")]
    public async Task<IActionResult> GetShipments([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null, [FromQuery] string? city = null, CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 50)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid pagination",
                detail: "page must be 1 or more and pageSize must be between 1 and 50.");

        var (items, total) = await shipmentService.GetShipmentsAsync(page, pageSize, status, city, cancellationToken);
        return Ok(ShipmentResponseMapper.ToPage(items, ShipmentResponseMapper.ToV1, page, pageSize, total));
    }

    /// <summary>Returns one shipment by tracking number.</summary>
    [HttpGet("{trackingNumber:trackingNumber}", Name = "GetShipmentV1")]
    [Authorize]
    [ProducesResponseType(typeof(ShipmentResponseV1), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [EndpointSummary("Get one shipment by tracking number")]
    [Tags("Shipments")]
    public async Task<IActionResult> GetShipment(string trackingNumber, CancellationToken cancellationToken)
    {
        var shipment = await shipmentService.GetShipmentAsync(trackingNumber, cancellationToken);
        return shipment is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Shipment not found",
                detail: $"No shipment exists with tracking number {trackingNumber}.")
            : Ok(ShipmentResponseMapper.ToV1(shipment));
    }

    /// <summary>Creates a shipment and returns its location in the Location header.</summary>
    [HttpPost(Name = "CreateShipmentV1")]
    [Consumes("application/json")]
    [Authorize(Roles = ApiRoles.Admin)]
    [ProducesResponseType(typeof(ShipmentResponseV1), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [EndpointSummary("Create a shipment")]
    [Tags("Shipments")]
    public async Task<IActionResult> CreateShipment([FromBody] CreateShipmentRequest request, CancellationToken cancellationToken)
    {
        var shipment = await shipmentService.CreateShipmentAsync(request.TrackingNumber, request.Weight,
            request.RecipientName, request.RecipientPhone, request.RecipientAddress, request.RecipientCity, request.Notes,
            cancellationToken);
        return CreatedAtRoute("GetShipmentV1", new { version = RouteData.Values["version"], trackingNumber = shipment.TrackingNumber },
            ShipmentResponseMapper.ToV1(shipment));
    }

    /// <summary>Replaces the editable details of a shipment that is still in Created status.</summary>
    [HttpPut("{trackingNumber:trackingNumber}", Name = "UpdateShipmentV1")]
    [Consumes("application/json")]
    [Authorize(Roles = ApiRoles.Admin)]
    [ProducesResponseType(typeof(ShipmentResponseV1), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [EndpointSummary("Replace editable shipment details")]
    [Tags("Shipments")]
    public async Task<IActionResult> UpdateShipment(string trackingNumber, [FromBody] UpdateShipmentRequest request,
        CancellationToken cancellationToken)
    {
        var shipment = await shipmentService.UpdateShipmentAsync(trackingNumber, request.Weight,
            request.RecipientName, request.RecipientPhone, request.RecipientAddress, request.RecipientCity, request.Notes,
            cancellationToken);
        return Ok(ShipmentResponseMapper.ToV1(shipment));
    }

    /// <summary>Moves a shipment to its next status (Created, PickedUp, InTransit, OutForDelivery, Delivered or Failed).</summary>
    [HttpPatch("{trackingNumber:trackingNumber}/status", Name = "UpdateShipmentStatusV1")]
    [Consumes("application/json")]
    [Authorize(Roles = ApiRoles.Courier)]
    [ProducesResponseType(typeof(ShipmentResponseV1), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [EndpointSummary("Change shipment status using an allowed transition")]
    [Tags("Shipments")]
    public async Task<IActionResult> UpdateShipmentStatus(string trackingNumber, [FromBody] UpdateStatusRequest request,
        CancellationToken cancellationToken)
    {
        var shipment = await shipmentService.UpdateShipmentStatusAsync(trackingNumber, request.Status, cancellationToken);
        return Ok(ShipmentResponseMapper.ToV1(shipment));
    }

    /// <summary>Deletes a shipment. Only allowed while the status is Created.</summary>
    [HttpDelete("{trackingNumber:trackingNumber}", Name = "DeleteShipmentV1")]
    [Authorize(Roles = ApiRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [EndpointSummary("Delete a shipment only while its status is Created")]
    [Tags("Shipments")]
    public async Task<IActionResult> DeleteShipment(string trackingNumber, CancellationToken cancellationToken)
    {
        await shipmentService.DeleteShipmentAsync(trackingNumber, cancellationToken);
        return NoContent();
    }

    /// <summary>Uploads a batch of offline scans. Re-sending a scan ID is safe: it is counted as a duplicate.</summary>
    /// <remarks>Every scan's courierId must match the courierId claim in the caller's token.</remarks>
    [HttpPost("{trackingNumber:trackingNumber}/scans", Name = "UploadScansV1")]
    [Consumes("application/json")]
    [Authorize(Roles = ApiRoles.Courier)]
    [ProducesResponseType(typeof(ScanUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [EndpointSummary("Upload offline scans idempotently")]
    [Tags("Scans")]
    public async Task<IActionResult> UploadScans(string trackingNumber, [FromBody] ScanUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (!CourierOwnership.OwnsAll(User, request))
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                detail: "A courier may upload scans only under their own courierId claim.");

        var result = await shipmentService.UploadScansAsync(trackingNumber, ShipmentResponseMapper.ToUploads(request),
            cancellationToken);
        return Ok(ShipmentResponseMapper.ToUploadResponse(result));
    }

    /// <summary>Returns the scan history for a shipment, newest first.</summary>
    [HttpGet("{trackingNumber:trackingNumber}/scans", Name = "GetScanHistoryV1")]
    [Authorize]
    [ProducesResponseType(typeof(ScanHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [EndpointSummary("Get scan history for a shipment")]
    [Tags("Scans")]
    public async Task<IActionResult> GetScanHistory(string trackingNumber, CancellationToken cancellationToken)
    {
        var scans = await shipmentService.GetScansAsync(trackingNumber, cancellationToken);
        return Ok(ShipmentResponseMapper.ToScanHistory(trackingNumber, scans));
    }
}
