using BatteryMes.Api.Services;
using BatteryMes.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace BatteryMes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController(
    ISqlSugarClient db, UnitService units, AlarmService alarms, OeeService oee) : ControllerBase
{
    /// <summary>Top-line KPIs for the dashboard header.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> Summary()
    {
        var today = DateTime.UtcNow.Date;
        var inProgress = await db.Queryable<Core.Entities.Unit>().Where(u => u.Status == "InProcess").CountAsync();
        var todayCount = await db.Queryable<Core.Entities.Unit>().Where(u => u.EnteredLineAt >= today).CountAsync();
        var ngToday = await db.Queryable<Core.Entities.Unit>().Where(u => u.Status == "Ng" && u.FinishedAt >= today).CountAsync();
        var (active, fault) = await alarms.ActiveAlarmCountsAsync();

        // Line OEE = average of station OEEs over last 8h.
        var stationOee = await oee.ComputeOeeAsync(DateTime.UtcNow.AddHours(-8), DateTime.UtcNow);
        var lineOee = stationOee.Count == 0 ? 0m : Math.Round(stationOee.Average(s => s.OeePct), 2);

        return Ok(new DashboardSummaryDto(inProgress, todayCount, ngToday,
            active, fault, lineOee, DateTime.UtcNow));
    }

    /// <summary>NG Pareto for analysis.</summary>
    [HttpGet("ng-pareto")]
    public async Task<ActionResult<IEnumerable<NgParetoItemDto>>> NgPareto(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await units.NgParetoAsync(from, to));

    /// <summary>Hourly production for today (for trend charts).</summary>
    [HttpGet("hourly-production")]
    public async Task<IActionResult> HourlyProduction([FromQuery] DateTime? date)
    {
        var d = (date ?? DateTime.UtcNow).Date;
        var events = await db.Queryable<Core.Entities.UnitEvent>()
            .Where(e => e.EventType == "Done" && e.PlcTimestamp >= d && e.PlcTimestamp < d.AddDays(1))
            .ToListAsync();
        var hourly = events.GroupBy(e => e.PlcTimestamp.Hour)
            .Select(g => new { hour = g.Key, count = g.Select(e => e.UnitId).Distinct().Count() })
            .OrderBy(x => x.hour);
        return Ok(hourly);
    }
}
