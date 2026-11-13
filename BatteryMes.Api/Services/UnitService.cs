using System.Text.Json;
using BatteryMes.Core.DTOs;
using BatteryMes.Core.Entities;
using BatteryMes.Core.Enums;
using SqlSugar;

namespace BatteryMes.Api.Services;

/// <summary>
/// Core production service: ingests PLC/gateway events, maintains the unit
/// state machine, stores genealogy (materials/tests), and serves traceability
/// queries. This is the heart of the MES.
/// </summary>
public class UnitService(ISqlSugarClient db)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    // ---------- Queries ----------

    public async Task<PagedResult<UnitDto>> QueryUnitsAsync(
        string? status, string? serial, string? workOrder, long? productId,
        DateTime? from, DateTime? to, PaginationQuery page, CancellationToken ct)
    {
        var q = db.Queryable<Unit>()
            .WhereIF(!string.IsNullOrWhiteSpace(status), u => u.Status == status)
            .WhereIF(!string.IsNullOrWhiteSpace(serial), u => u.SerialNumber.Contains(serial!))
            .WhereIF(!string.IsNullOrWhiteSpace(workOrder), u => u.WorkOrderNumber == workOrder)
            .WhereIF(productId.HasValue, u => u.ProductId == productId)
            .WhereIF(from.HasValue, u => u.EnteredLineAt >= from)
            .WhereIF(to.HasValue, u => u.EnteredLineAt <= to)
            .OrderByDescending(u => u.Id);

        var total = await q.CountAsync();
        var rows = await q.Skip(page.Skip).Take(Math.Clamp(page.PageSize, 1, 500)).ToListAsync();
        var items = rows.Select(u => new UnitDto(u.Id, u.SerialNumber, u.WorkOrderNumber,
            u.ProductId, u.Status, u.NgStation, u.NgCode, u.CycleTimeMs,
            u.EnteredLineAt, u.FinishedAt)).ToList();
        return new PagedResult<UnitDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<UnitDetailDto?> GetUnitDetailAsync(long id)
    {
        var unit = await db.Queryable<Unit>().InSingleAsync(id);
        if (unit is null) return null;

        var eventRows = await db.Queryable<UnitEvent>()
            .Where(e => e.UnitId == id)
            .OrderBy(e => e.PlcTimestamp)
            .ToListAsync();

        var opIds = eventRows.Where(e => e.OperatorId.HasValue)
            .Select(e => e.OperatorId!.Value).Distinct().ToList();
        var opNames = opIds.Count == 0
            ? new Dictionary<long, string>()
            : (await db.Queryable<Operator>().Where(o => opIds.Contains(o.Id)).ToListAsync())
                .ToDictionary(o => o.Id, o => o.Name);

        var events = eventRows.Select(e => new UnitEventDto(e.Id, e.UnitId, e.StationNo,
            e.EventType, e.StepNo, e.PlcTimestamp, e.MesReceivedAt, e.Value,
            e.OperatorId, e.OperatorId.HasValue ? opNames.GetValueOrDefault(e.OperatorId.Value) : null))
            .ToList();

        var materialRows = await db.Queryable<UnitMaterial>().Where(m => m.UnitId == id).ToListAsync();
        var materials = materialRows.Select(m => new UnitMaterialDto(m.Id, m.MaterialType,
            m.LotNumber, m.QtyUsed, m.UnitOfMeasure, m.ConsumedAt)).ToList();

        var testRows = await db.Queryable<UnitTest>().Where(t => t.UnitId == id)
            .OrderBy(t => t.TestedAt).ToListAsync();
        var tests = testRows.Select(t => new UnitTestDto(t.Id, t.StationNo, t.TestType,
            t.MeasuredValue, t.SpecMin, t.SpecMax, t.Result, t.TestedAt)).ToList();

        return new UnitDetailDto(
            new UnitDto(unit.Id, unit.SerialNumber, unit.WorkOrderNumber, unit.ProductId,
                unit.Status, unit.NgStation, unit.NgCode, unit.CycleTimeMs,
                unit.EnteredLineAt, unit.FinishedAt),
            events, materials, tests);
    }

    public async Task<Unit?> FindBySerialAsync(string serial) =>
        await db.Queryable<Unit>().Where(u => u.SerialNumber == serial).FirstAsync();

    // ---------- Ingestion ----------

    /// <summary>
    /// Ingest a batch of PLC events. Applies unit state machine transitions,
    /// dedupes via IdempotencyKey, and returns per-batch stats.
    /// </summary>
    public async Task<IngestBatchResponse> IngestEventsAsync(IngestBatchRequest request)
    {
        var accepted = 0; var duplicates = 0; var errors = 0;
        var errorDetails = new List<string>();

        foreach (var ev in request.Events)
        {
            try
            {
                if (!Enum.TryParse<EventType>(ev.EventType, ignoreCase: true, out var et))
                {
                    errors++; errorDetails.Add($"Unknown EventType '{ev.EventType}'");
                    continue;
                }

                if (!string.IsNullOrEmpty(ev.IdempotencyKey) &&
                    await db.Queryable<UnitEvent>().AnyAsync(e => e.IdempotencyKey == ev.IdempotencyKey))
                {
                    duplicates++;
                    continue;
                }

                var unit = await EnsureUnitAsync(ev.UnitSerial, ev.PlcTimestamp);
                if (unit is null) { errors++; errorDetails.Add($"Cannot resolve unit '{ev.UnitSerial}'"); continue; }

                db.Ado.BeginTran();
                try
                {
                    var plcTs = ev.PlcTimestamp ?? DateTime.UtcNow;
                    db.Insertable(new UnitEvent
                    {
                        UnitId = unit.Id,
                        StationNo = ev.StationNo,
                        EventType = et.ToString(),
                        StepNo = ev.StepNo,
                        PlcTimestamp = plcTs,
                        Value = ev.Value,
                        OperatorId = ev.OperatorId,
                        IdempotencyKey = ev.IdempotencyKey
                    }).ExecuteCommand();

                    ApplyStateTransition(unit, et, ev.StationNo, ev.StepNo, ev.CycleTimeMs, plcTs);
                    await db.Updateable(unit).ExecuteCommandAsync();
                    db.Ado.CommitTran();
                    accepted++;
                }
                catch
                {
                    db.Ado.RollbackTran();
                    throw;
                }
            }
            catch (Exception ex)
            {
                errors++;
                errorDetails.Add($"{ev.UnitSerial}/STN{ev.StationNo}: {ex.Message}");
            }
        }

        return new IngestBatchResponse(accepted, duplicates, errors, errorDetails);
    }

    private async Task<Unit?> EnsureUnitAsync(string serial, DateTime? plcTs)
    {
        var unit = await FindBySerialAsync(serial);
        if (unit is not null) return unit;

        // First-seen serial: create unit record (units normally created at
        // STN-01 CarrierIn; creation here keeps ingestion resilient).
        var ts = plcTs ?? DateTime.UtcNow;
        var created = new Unit
        {
            SerialNumber = serial,
            Status = "Pending",
            EnteredLineAt = ts
        };
        created.Id = await db.Insertable(created).ExecuteReturnBigIdentityAsync();
        return created;
    }

    /// <summary>
    /// Derives unit.status from event stream per docs/06-interface-mapping.md §3.
    /// </summary>
    private static void ApplyStateTransition(Unit unit, EventType et, int stationNo,
        int? stepNo, int? cycleTimeMs, DateTime ts)
    {
        switch (et)
        {
            case EventType.CarrierIn when unit.Status == "Pending":
                unit.Status = "InProcess";
                unit.EnteredLineAt = ts;
                break;
            case EventType.QcFail:
                unit.Status = "Ng";
                unit.NgStation = stationNo;
                unit.NgCode = stepNo;
                unit.FinishedAt ??= ts;
                break;
            case EventType.Done when stationNo == 4:
                unit.Status = "Done";
                unit.FinishedAt = ts;
                break;
            case EventType.Abort:
                unit.Status = "Scrapped";
                unit.FinishedAt ??= ts;
                break;
        }
        if (cycleTimeMs.HasValue) unit.CycleTimeMs += cycleTimeMs.Value;
    }

    public async Task<int> AddMaterialAsync(IngestMaterialRequest req)
    {
        var unit = await FindBySerialAsync(req.UnitSerial);
        if (unit is null) return -1;
        return await db.Insertable(new UnitMaterial
        {
            UnitId = unit.Id,
            MaterialType = req.MaterialType,
            LotNumber = req.LotNumber,
            QtyUsed = req.QtyUsed,
            UnitOfMeasure = req.UnitOfMeasure,
            ConsumedAt = req.ConsumedAt ?? DateTime.UtcNow
        }).ExecuteCommandAsync();
    }

    public async Task<int> AddTestAsync(IngestTestRequest req)
    {
        var unit = await FindBySerialAsync(req.UnitSerial);
        if (unit is null) return -1;

        // Evaluate PASS/FAIL against product spec when limits are provided.
        var result = "PASS";
        if (req.SpecMin.HasValue && req.MeasuredValue < req.SpecMin.Value) result = "FAIL";
        else if (req.SpecMax.HasValue && req.MeasuredValue > req.SpecMax.Value) result = "FAIL";

        return await db.Insertable(new UnitTest
        {
            UnitId = unit.Id,
            StationNo = req.StationNo,
            TestType = req.TestType,
            MeasuredValue = req.MeasuredValue,
            SpecMin = req.SpecMin,
            SpecMax = req.SpecMax,
            Result = result,
            TestedAt = req.TestedAt ?? DateTime.UtcNow
        }).ExecuteCommandAsync();
    }

    /// <summary>NG Pareto: count of NG units grouped by station+reason.</summary>
    public async Task<IReadOnlyList<NgParetoItemDto>> NgParetoAsync(DateTime? from, DateTime? to)
    {
        var rows = await db.Queryable<Unit>()
            .Where(u => u.Status == "Ng")
            .WhereIF(from.HasValue, u => u.FinishedAt >= from || u.EnteredLineAt >= from)
            .WhereIF(to.HasValue, u => u.FinishedAt <= to || u.EnteredLineAt <= to)
            .ToListAsync();
        return rows.GroupBy(u => (u.NgStation ?? 0, u.NgCode ?? 0))
            .Select(g => new NgParetoItemDto(g.Key.Item2, g.Key.Item1, g.Count()))
            .OrderByDescending(r => r.Count).ToList();
    }
}
