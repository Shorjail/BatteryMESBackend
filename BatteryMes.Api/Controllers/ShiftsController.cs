using BatteryMes.Api.Services;
using BatteryMes.Core.DTOs;
using BatteryMes.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace BatteryMes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ShiftsController(ISqlSugarClient db, OeeService oee) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ShiftDto>>> List()
        => Ok(await oee.ListShiftsAsync());

    [HttpPost]
    [Authorize(Roles = "Supervisor,Admin")]
    public async Task<ActionResult<ShiftDto>> Create(CreateShiftRequest req)
    {
        var shift = await oee.CreateShiftAsync(req.Date, req.ShiftNo, req.StartAt, req.EndAt);
        return Ok(new ShiftDto(shift.Id, shift.ShiftDate, shift.ShiftNo, shift.StartAt, shift.EndAt));
    }

    /// <summary>Generate (or regenerate) the OEE shift report for a shift.</summary>
    [HttpPost("{id:long}/report")]
    [Authorize(Roles = "Supervisor,Admin")]
    public async Task<ActionResult<IEnumerable<ShiftReportDto>>> GenerateReport(long id)
    {
        var reports = await oee.GenerateShiftReportAsync(id);
        return reports.Count == 0 ? NotFound(new { error = "Shift not found" }) : Ok(reports);
    }

    /// <summary>Persisted shift reports.</summary>
    [HttpGet("reports")]
    public async Task<ActionResult<IEnumerable<ShiftReportDto>>> Reports([FromQuery] long? shiftId)
    {
        var rows = await db.Queryable<ShiftReport>()
            .WhereIF(shiftId.HasValue, r => r.ShiftId == shiftId)
            .OrderByDescending(r => r.Id).Take(500).ToListAsync();
        return Ok(rows.Select(r => new ShiftReportDto(r.Id, r.ShiftId, r.StationNo,
            r.UnitsProduced, r.UnitsNg, r.AvailabilityPct, r.PerformancePct,
            r.QualityPct, r.OeePct, r.DowntimePareto, r.GeneratedAt)));
    }
}

public record CreateShiftRequest(DateTime Date, int ShiftNo, DateTime StartAt, DateTime EndAt);
