using BatteryMes.Api.Services;
using BatteryMes.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BatteryMes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AlarmsController(AlarmService alarms) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AlarmDto>>> Query(
        [FromQuery] int? stationNo, [FromQuery] string? state,
        [FromQuery] int? severity, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await alarms.QueryAsync(new AlarmQuery(stationNo, state, severity,
            new PaginationQuery(page, pageSize))));

    [HttpGet("active")]
    public async Task<ActionResult<PagedResult<AlarmDto>>> Active(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await alarms.QueryAsync(new AlarmQuery(null, null, null, new PaginationQuery(page, pageSize))));

    /// <summary>Acknowledge an alarm (operator/supervisor action).</summary>
    [HttpPost("{id:long}/ack")]
    [Authorize(Roles = "Operator,Supervisor,Admin")]
    public async Task<IActionResult> Ack(long id)
        => await alarms.AckAsync(id) ? NoContent() : NotFound();
}
