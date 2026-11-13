using BatteryMes.Api.Services;
using BatteryMes.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace BatteryMes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UnitsController(UnitService units, ISqlSugarClient db) : ControllerBase
{
    /// <summary>Search/filter units with pagination.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<UnitDto>>> Query(
        [FromQuery] string? status, [FromQuery] string? serial,
        [FromQuery] string? workOrder, [FromQuery] long? productId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var result = await units.QueryUnitsAsync(status, serial, workOrder,
            productId, from, to, new PaginationQuery(page, pageSize), HttpContext.RequestAborted);
        return Ok(result);
    }

    /// <summary>Full unit genealogy: events, materials, tests.</summary>
    [HttpGet("{id:long}")]
    public async Task<ActionResult<UnitDetailDto>> GetById(long id)
    {
        var dto = await units.GetUnitDetailAsync(id);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>Lookup by serial number.</summary>
    [HttpGet("serial/{serial}")]
    public async Task<ActionResult<UnitDetailDto>> GetBySerial(string serial)
    {
        var unit = await units.FindBySerialAsync(serial);
        if (unit is null) return NotFound();
        return await GetById(unit.Id);
    }

    [HttpPost]
    [Authorize(Roles = "Operator,Supervisor,Admin,Gateway")]
    public async Task<ActionResult<UnitDto>> Create(CreateUnitRequest req)
    {
        var unit = new Core.Entities.Unit
        {
            SerialNumber = req.SerialNumber,
            WorkOrderNumber = req.WorkOrderNumber,
            ProductId = req.ProductId,
            Status = "Pending",
            EnteredLineAt = DateTime.UtcNow
        };
        unit.Id = await db.Insertable(unit).ExecuteReturnBigIdentityAsync();
        return CreatedAtAction(nameof(GetById), new { id = unit.Id },
            new UnitDto(unit.Id, unit.SerialNumber, unit.WorkOrderNumber, unit.ProductId,
                unit.Status, null, null, 0, unit.EnteredLineAt, null));
    }

    [HttpPatch("{id:long}/status")]
    [Authorize(Roles = "Operator,Supervisor,Admin")]
    public async Task<IActionResult> UpdateStatus(long id, UpdateUnitStatusRequest req)
    {
        var unit = await db.Queryable<Core.Entities.Unit>().InSingleAsync(id);
        if (unit is null) return NotFound();
        unit.Status = req.Status;
        unit.NgStation = req.NgStation;
        unit.NgCode = req.NgCode;
        if (req.Status is "Done" or "Ng" or "Scrapped") unit.FinishedAt ??= DateTime.UtcNow;
        await db.Updateable(unit).ExecuteCommandAsync();
        return NoContent();
    }
}
