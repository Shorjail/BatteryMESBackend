using System.Text.Json;
using BatteryMes.Core.DTOs;
using BatteryMes.Core.Entities;
using SqlSugar;

namespace BatteryMes.Api.Services;

/// <summary>
/// OEE & shift reporting. Availability comes from PLC line-state snapshots,
/// performance from actual vs. theoretical cycle time (derived from event
/// timestamps), quality from pass/NG unit counts.
/// </summary>
public class OeeService(ISqlSugarClient db)
{
    /// <summary>Live station states for dashboard.</summary>
    public async Task<IReadOnlyList<StationStateDto>> GetStationStatesAsync(DateTime? since = null)
    {
        var today = DateTime.UtcNow.Date;
        var stations = await db.Queryable<Station>().Where(s => s.IsActive).ToListAsync();

        // Unit currently at each station = InProcess unit whose LATEST event
        // belongs to that station. Derived from the event stream since the
        // PLC doesn't push per-station occupancy directly.
        var inProcess = await db.Queryable<Unit>().Where(u => u.Status == "InProcess").ToListAsync();
        var unitIds = inProcess.Select(u => u.Id).ToList();
        var events = unitIds.Count == 0 ? new List<UnitEvent>()
            : await db.Queryable<UnitEvent>().Where(e => unitIds.Contains(e.UnitId)).ToListAsync();
        var stationOccupancy = new Dictionary<int, string>();
        var groups = events.GroupBy(e => e.UnitId)
            .Select(g => g.OrderByDescending(e => e.Id).First())
            .OrderByDescending(e => e.Id); // newest unit wins per station
        foreach (var last in groups)
        {
            if (last.EventType is "CarrierIn" or "ProcessStart" or "StepDone" or "QcStart"
                && !stationOccupancy.ContainsKey(last.StationNo))
            {
                stationOccupancy[last.StationNo] = inProcess.First(u => u.Id == last.UnitId).SerialNumber;
            }
        }

        var result = new List<StationStateDto>();

        foreach (var st in stations)
        {
            var latest = await db.Queryable<LineStateSnapshot>()
                .Where(s => s.StationNo == st.StationNo)
                .OrderByDescending(s => s.CapturedAt).FirstAsync();

            var todayStart = today;
            var unitsToday = await db.Queryable<UnitEvent>()
                .Where(e => e.StationNo == st.StationNo && e.EventType == "Done" && e.PlcTimestamp >= todayStart)
                .Select(e => e.UnitId).Distinct().CountAsync();
            var ngToday = await db.Queryable<Unit>()
                .Where(u => u.NgStation == st.StationNo && u.Status == "Ng" && u.FinishedAt >= todayStart)
                .CountAsync();

            stationOccupancy.TryGetValue(st.StationNo, out var currentSerial);

            result.Add(new StationStateDto(
                st.StationNo,
                StateName(latest?.State),
                currentSerial, null,
                unitsToday, ngToday));
        }
        return result;
    }

    private static string StateName(int? s) => s switch
    {
        0 => "Idle", 1 => "CarrierIn", 2 => "Processing", 3 => "QualityCheck",
        4 => "Unload", 5 => "Reject", 6 => "Fault", _ => "Unknown"
    };

    public async Task RecordLineStateAsync(LineStatePushRequest req)
    {
        var rows = new List<LineStateSnapshot>();
        var now = DateTime.UtcNow;
        if (req.StationStates is not null)
        {
            foreach (var kv in req.StationStates)
                rows.Add(new LineStateSnapshot { StationNo = kv.Key, State = StateCode(kv.Value), CapturedAt = now });
        }
        else
        {
            rows.Add(new LineStateSnapshot { StationNo = 0, State = req.State, CapturedAt = now });
        }
        await db.Insertable(rows).ExecuteCommandAsync();
    }

    private static int StateCode(string s) => s.ToLowerInvariant() switch
    {
        "idle" => 0, "carrierin" => 1, "processing" => 2, "qualitycheck" => 3,
        "unload" => 4, "reject" => 5, "fault" => 6, _ => 0
    };

    /// <summary>
    /// Compute OEE per station for a time window. Availability = share of
    /// snapshots in non-fault running states; performance = theoretical /
    /// actual avg station cycle (CarrierIn→Released dwell); quality =
    /// pass/(pass+fail) at that station.
    /// </summary>
    public async Task<IReadOnlyList<StationOeeDto>> ComputeOeeAsync(DateTime from, DateTime to)
    {
        var stations = await db.Queryable<Station>().Where(s => s.IsActive).ToListAsync();
        var result = new List<StationOeeDto>();

        foreach (var st in stations)
        {
            // --- quality ---
            var passUnits = await db.Queryable<UnitEvent>()
                .Where(e => e.StationNo == st.StationNo &&
                            (e.EventType == "QcPass" || e.EventType == "Released" || e.EventType == "Done"))
                .Where(e => e.PlcTimestamp >= from && e.PlcTimestamp <= to)
                .Select(e => e.UnitId).Distinct().CountAsync();
            var failUnits = await db.Queryable<UnitEvent>()
                .Where(e => e.StationNo == st.StationNo && e.EventType == "QcFail")
                .Where(e => e.PlcTimestamp >= from && e.PlcTimestamp <= to)
                .Select(e => e.UnitId).Distinct().CountAsync();
            var totalUnits = passUnits + failUnits;
            var quality = totalUnits == 0 ? 100m : Math.Round(100m * passUnits / totalUnits, 2);

            // --- performance: avg dwell CarrierIn→Released per unit ---
            var dwells = await db.Queryable<UnitEvent>()
                .Where(e => e.StationNo == st.StationNo &&
                            (e.EventType == "CarrierIn" || e.EventType == "Released" || e.EventType == "QcFail"))
                .Where(e => e.PlcTimestamp >= from && e.PlcTimestamp <= to)
                .OrderBy(e => e.UnitId).OrderBy(e => e.PlcTimestamp)
                .ToListAsync();

            var avgCycleMs = 0m;
            var open = new Dictionary<long, DateTime>();
            var dwellList = new List<double>();
            foreach (var e in dwells)
            {
                if (e.EventType == "CarrierIn") open[e.UnitId] = e.PlcTimestamp;
                else if (open.Remove(e.UnitId, out var start))
                    dwellList.Add((e.PlcTimestamp - start).TotalMilliseconds);
            }
            if (dwellList.Count > 0) avgCycleMs = (decimal)dwellList.Average();
            var performance = avgCycleMs <= 0 ? 100m
                : Math.Round(Math.Min(100m, 100m * st.TheoreticalCycleMs / avgCycleMs), 2);

            // --- availability: snapshot state mix ---
            var snaps = await db.Queryable<LineStateSnapshot>()
                .Where(s => s.StationNo == st.StationNo && s.CapturedAt >= from && s.CapturedAt <= to)
                .ToListAsync();
            decimal availability = 100m;
            if (snaps.Count > 0)
            {
                var running = snaps.Count(s => s.State is >= 1 and <= 4);
                availability = Math.Round(100m * running / snaps.Count, 2);
            }

            var oee = Math.Round(availability * performance * quality / 10000m, 2);
            result.Add(new StationOeeDto(st.StationNo, st.Name, availability, performance,
                quality, oee, passUnits, failUnits, (int)avgCycleMs));
        }
        return result;
    }

    /// <summary>Persist per-station OEE rows into shift_reports for a shift window.</summary>
    public async Task<IReadOnlyList<ShiftReportDto>> GenerateShiftReportAsync(long shiftId)
    {
        var shift = await db.Queryable<Shift>().InSingleAsync(shiftId);
        if (shift is null) return Array.Empty<ShiftReportDto>();

        // Idempotent: replace prior generated rows for this shift.
        await db.Deleteable<ShiftReport>().Where(r => r.ShiftId == shiftId).ExecuteCommandAsync();

        var oee = await ComputeOeeAsync(shift.StartAt, shift.EndAt);
        var reports = oee.Select(o => new ShiftReport
        {
            ShiftId = shiftId,
            StationNo = o.StationNo,
            UnitsProduced = o.UnitsProduced,
            UnitsNg = o.UnitsNg,
            AvailabilityPct = o.AvailabilityPct,
            PerformancePct = o.PerformancePct,
            QualityPct = o.QualityPct,
            OeePct = o.OeePct
        }).ToList();
        await db.Insertable(reports).ExecuteCommandAsync();

        var saved = await db.Queryable<ShiftReport>().Where(r => r.ShiftId == shiftId)
            .OrderBy(r => r.StationNo).ToListAsync();
        return saved.Select(r => new ShiftReportDto(r.Id, r.ShiftId, r.StationNo,
            r.UnitsProduced, r.UnitsNg, r.AvailabilityPct, r.PerformancePct,
            r.QualityPct, r.OeePct, r.DowntimePareto, r.GeneratedAt)).ToList();
    }

    public async Task<IReadOnlyList<ShiftDto>> ListShiftsAsync(int take = 30) =>
        (await db.Queryable<Shift>().OrderByDescending(s => s.ShiftDate).Take(take).ToListAsync())
        .Select(s => new ShiftDto(s.Id, s.ShiftDate, s.ShiftNo, s.StartAt, s.EndAt)).ToList();

    public async Task<Shift> CreateShiftAsync(DateTime date, int shiftNo, DateTime start, DateTime end)
    {
        var s = new Shift { ShiftDate = date.Date, ShiftNo = shiftNo, StartAt = start, EndAt = end };
        s.Id = await db.Insertable(s).ExecuteReturnBigIdentityAsync();
        return s;
    }
}
