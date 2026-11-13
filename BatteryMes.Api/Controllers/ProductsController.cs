using BatteryMes.Core.DTOs;
using BatteryMes.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace BatteryMes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProductsController(ISqlSugarClient db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductDto>>> List()
        => Ok((await db.Queryable<Product>().Where(p => p.IsActive).OrderBy(p => p.Code).ToListAsync()).Select(ToDto));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ProductDto>> Get(long id)
    {
        var p = await db.Queryable<Product>().InSingleAsync(id);
        return p is null ? NotFound() : Ok(ToDto(p));
    }

    /// <summary>Create or update by product code (spec limits are data-driven).</summary>
    [HttpPut]
    [Authorize(Roles = "Supervisor,Admin")]
    public async Task<IActionResult> Upsert(UpsertProductRequest req)
    {
        var existing = await db.Queryable<Product>().Where(p => p.Code == req.Code).FirstAsync();
        if (existing is null)
        {
            var p = new Product
            {
                Code = req.Code, Name = req.Name,
                OcvMinMv = req.OcvMinMv, OcvMaxMv = req.OcvMaxMv, IrMaxMohm = req.IrMaxMohm,
                WeldEnergyMinJ = req.WeldEnergyMinJ, WeldEnergyMaxJ = req.WeldEnergyMaxJ,
                FillWeightTargetG = req.FillWeightTargetG, FillWeightTolG = req.FillWeightTolG,
                LeakMaxSccm = req.LeakMaxSccm, VisionMinScore = req.VisionMinScore,
                IsActive = req.IsActive
            };
            p.Id = await db.Insertable(p).ExecuteReturnBigIdentityAsync();
            return CreatedAtAction(nameof(Get), new { id = p.Id }, ToDto(p));
        }

        existing.Name = req.Name;
        existing.OcvMinMv = req.OcvMinMv; existing.OcvMaxMv = req.OcvMaxMv; existing.IrMaxMohm = req.IrMaxMohm;
        existing.WeldEnergyMinJ = req.WeldEnergyMinJ; existing.WeldEnergyMaxJ = req.WeldEnergyMaxJ;
        existing.FillWeightTargetG = req.FillWeightTargetG; existing.FillWeightTolG = req.FillWeightTolG;
        existing.LeakMaxSccm = req.LeakMaxSccm; existing.VisionMinScore = req.VisionMinScore;
        existing.IsActive = req.IsActive;
        await db.Updateable(existing).ExecuteCommandAsync();
        return NoContent();
    }

    private static ProductDto ToDto(Product p) => new(p.Id, p.Code, p.Name,
        p.OcvMinMv, p.OcvMaxMv, p.IrMaxMohm, p.WeldEnergyMinJ, p.WeldEnergyMaxJ,
        p.FillWeightTargetG, p.FillWeightTolG, p.LeakMaxSccm, p.VisionMinScore, p.IsActive);
}
