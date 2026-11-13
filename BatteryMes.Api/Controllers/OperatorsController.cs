using System.Text.Json;
using BatteryMes.Core.DTOs;
using BatteryMes.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace BatteryMes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OperatorsController(ISqlSugarClient db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OperatorDto>>> List()
    {
        var ops = await db.Queryable<Operator>().Where(o => o.IsActive).OrderBy(o => o.Name).ToListAsync();
        return Ok(ops.Select(ToDto));
    }

    [HttpPost]
    [Authorize(Roles = "Supervisor,Admin")]
    public async Task<ActionResult<OperatorDto>> Create(CreateOperatorRequest req)
    {
        var op = new Operator
        {
            BadgeId = req.BadgeId,
            Name = req.Name,
            StationCertifications = JsonSerializer.Serialize(req.StationCertifications)
        };
        op.Id = await db.Insertable(op).ExecuteReturnBigIdentityAsync();
        return CreatedAtAction(nameof(List), ToDto(op));
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = "Supervisor,Admin")]
    public async Task<IActionResult> Update(long id, UpdateOperatorRequest req)
    {
        var op = await db.Queryable<Operator>().InSingleAsync(id);
        if (op is null) return NotFound();
        op.Name = req.Name;
        op.StationCertifications = JsonSerializer.Serialize(req.StationCertifications);
        op.IsActive = req.IsActive;
        await db.Updateable(op).ExecuteCommandAsync();
        return NoContent();
    }

    private static OperatorDto ToDto(Operator o) => new(o.Id, o.BadgeId, o.Name,
        string.IsNullOrEmpty(o.StationCertifications)
            ? Array.Empty<int>()
            : JsonSerializer.Deserialize<int[]>(o.StationCertifications) ?? Array.Empty<int>(),
        o.IsActive);
}
