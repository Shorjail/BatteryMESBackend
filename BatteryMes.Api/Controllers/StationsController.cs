using BatteryMes.Api.Services;
using BatteryMes.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace BatteryMes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StationsController(ISqlSugarClient db, OeeService oee) : ControllerBase
{
    /// <summary>Station master data.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StationDto>>> List()
    {
        var stations = await db.Queryable<Core.Entities.Station>().Where(s => s.IsActive)
            .OrderBy(s => s.StationNo).ToListAsync();
        return Ok(stations.Select(s => new StationDto(s.Id, s.StationNo, s.Name,
            s.ProcessStep, s.TheoreticalCycleMs, s.IsActive, null)));
    }

    /// <summary>Live station states + today counters (dashboard).</summary>
    [HttpGet("live")]
    public async Task<ActionResult<IEnumerable<StationStateDto>>> Live()
        => Ok(await oee.GetStationStatesAsync());

    /// <summary>OEE per station for a window (default: last 8h).</summary>
    [HttpGet("oee")]
    public async Task<ActionResult<IEnumerable<StationOeeDto>>> Oee(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var f = from ?? DateTime.UtcNow.AddHours(-8);
        var t = to ?? DateTime.UtcNow;
        return Ok(await oee.ComputeOeeAsync(f, t));
    }
}
