using BatteryMes.Core.DTOs;
using BatteryMes.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace BatteryMes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkOrdersController(ISqlSugarClient db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WorkOrderDto>>> List([FromQuery] string? status)
    {
        var orders = await db.Queryable<WorkOrder>()
            .WhereIF(!string.IsNullOrWhiteSpace(status), w => w.Status == status)
            .OrderByDescending(w => w.Id)
            .ToListAsync();
        var productIds = orders.Select(o => o.ProductId).Distinct().ToList();
        var products = await db.Queryable<Product>().Where(p => productIds.Contains(p.Id)).ToListAsync();
        var productCodes = products.ToDictionary(p => p.Id, p => p.Code);

        return Ok(orders.Select(w => new WorkOrderDto(w.Id, w.WoNumber, w.ProductId,
            productCodes.GetValueOrDefault(w.ProductId), w.TargetQty, w.Status,
            w.PlannedStart, w.PlannedEnd, w.CreatedAt)));
    }

    [HttpPost]
    [Authorize(Roles = "Supervisor,Admin")]
    public async Task<ActionResult<WorkOrderDto>> Create(CreateWorkOrderRequest req)
    {
        var product = await db.Queryable<Product>().InSingleAsync(req.ProductId);
        if (product is null) return BadRequest(new { error = "Product not found" });

        var wo = new WorkOrder
        {
            WoNumber = req.WoNumber,
            ProductId = req.ProductId,
            TargetQty = req.TargetQty,
            Status = "Planned",
            PlannedStart = req.PlannedStart,
            PlannedEnd = req.PlannedEnd
        };
        wo.Id = await db.Insertable(wo).ExecuteReturnBigIdentityAsync();
        return CreatedAtAction(nameof(List),
            new WorkOrderDto(wo.Id, wo.WoNumber, wo.ProductId, product.Code,
                wo.TargetQty, wo.Status, wo.PlannedStart, wo.PlannedEnd, wo.CreatedAt));
    }

    [HttpPut("{id:long}/status")]
    [Authorize(Roles = "Supervisor,Admin")]
    public async Task<IActionResult> SetStatus(long id, [FromBody] string status)
    {
        var wo = await db.Queryable<WorkOrder>().InSingleAsync(id);
        if (wo is null) return NotFound();
        wo.Status = status.Trim('"');
        await db.Updateable(wo).UpdateColumns(x => new { x.Status }).ExecuteCommandAsync();
        return NoContent();
    }
}
