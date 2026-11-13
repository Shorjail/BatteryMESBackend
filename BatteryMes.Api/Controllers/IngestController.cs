using BatteryMes.Api.Services;
using BatteryMes.Core.DTOs;
using BatteryMes.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace BatteryMes.Api.Controllers;

/// <summary>
/// Machine-facing ingestion endpoints for the edge gateway.
/// Auth: JWT with role Gateway/Admin, OR X-Api-Key header matching
/// configuration "Gateway:ApiKey" (for simple PLC-gateway clients).
/// </summary>
[ApiController]
[Route("api/ingest")]
public class IngestController(
    UnitService units, AlarmService alarms, OeeService oee,
    IConfiguration config) : ControllerBase
{
    private bool IsGatewayAuthorized()
    {
        if (User.IsInRole("Gateway") || User.IsInRole("Admin") || User.IsInRole("Supervisor"))
            return true;
        var key = Request.Headers["X-Api-Key"].ToString();
        var expected = config["Gateway:ApiKey"];
        return !string.IsNullOrEmpty(expected) && key == expected;
    }

    [HttpPost("events")]
    public async Task<ActionResult<IngestBatchResponse>> Events(IngestBatchRequest req)
    {
        if (!IsGatewayAuthorized()) return Unauthorized(new { error = "Gateway credentials required" });
        return Ok(await units.IngestEventsAsync(req));
    }

    [HttpPost("events/single")]
    public async Task<ActionResult<IngestBatchResponse>> SingleEvent(IngestEventRequest req)
    {
        if (!IsGatewayAuthorized()) return Unauthorized(new { error = "Gateway credentials required" });
        return Ok(await units.IngestEventsAsync(new IngestBatchRequest([req])));
    }

    [HttpPost("materials")]
    public async Task<IActionResult> Materials(IngestMaterialRequest req)
    {
        if (!IsGatewayAuthorized()) return Unauthorized(new { error = "Gateway credentials required" });
        var n = await units.AddMaterialAsync(req);
        return n < 0 ? NotFound(new { error = $"Unit '{req.UnitSerial}' not found" }) : Ok(new { inserted = n });
    }

    [HttpPost("tests")]
    public async Task<IActionResult> Tests(IngestTestRequest req)
    {
        if (!IsGatewayAuthorized()) return Unauthorized(new { error = "Gateway credentials required" });
        var n = await units.AddTestAsync(req);
        return n < 0 ? NotFound(new { error = $"Unit '{req.UnitSerial}' not found" }) : Ok(new { inserted = n });
    }

    [HttpPost("alarms")]
    public async Task<IActionResult> Alarms(IngestAlarmRequest req)
    {
        if (!IsGatewayAuthorized()) return Unauthorized(new { error = "Gateway credentials required" });
        var n = await alarms.IngestAsync(req);
        return Ok(new { affected = n });
    }

    /// <summary>Periodic line/station state push for OEE availability calc.</summary>
    [HttpPost("line-state")]
    public async Task<IActionResult> LineState(LineStatePushRequest req)
    {
        if (!IsGatewayAuthorized()) return Unauthorized(new { error = "Gateway credentials required" });
        await oee.RecordLineStateAsync(req);
        return NoContent();
    }
}
