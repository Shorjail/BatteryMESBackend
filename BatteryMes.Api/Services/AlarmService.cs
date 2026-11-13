using BatteryMes.Core.DTOs;
using BatteryMes.Core.Entities;
using SqlSugar;

namespace BatteryMes.Api.Services;

/// <summary>Alarm lifecycle: raise/ack/clear, mirroring PLC alarm table.</summary>
public class AlarmService(ISqlSugarClient db)
{
    public async Task<PagedResult<AlarmDto>> QueryAsync(AlarmQuery query)
    {
        var q = db.Queryable<Alarm>()
            .WhereIF(query.StationNo.HasValue, a => a.StationNo == query.StationNo)
            .WhereIF(!string.IsNullOrWhiteSpace(query.State), a => a.State == query.State)
            .WhereIF(query.Severity.HasValue, a => a.Severity == query.Severity)
            .OrderByDescending(a => a.RaisedAt);

        var page = query.Page ?? new PaginationQuery();
        var total = await q.CountAsync();
        var rows = await q.Skip(page.Skip).Take(Math.Clamp(page.PageSize, 1, 500)).ToListAsync();
        var items = rows.Select(a => new AlarmDto(a.Id, a.PlcAlarmId, a.StationNo, a.Severity,
            a.State, a.Text, a.RaisedAt, a.AckedAt, a.ClearedAt, a.DurationSec)).ToList();
        return new PagedResult<AlarmDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<Alarm?> GetAsync(long id) => await db.Queryable<Alarm>().InSingleAsync(id);

    public async Task<int> IngestAsync(IngestAlarmRequest req)
    {
        var now = DateTime.UtcNow;
        var plcTs = req.PlcTimestamp ?? now;

        return req.AlarmAction.ToLowerInvariant() switch
        {
            "raise" => await RaiseAsync(req, plcTs, now),
            "ack" => await TransitionAsync(req, s => s == "Active", a =>
            {
                a.State = "Acked"; a.AckedAt = now;
            }),
            "clear" => await TransitionAsync(req, s => s is "Active" or "Acked", a =>
            {
                a.State = "Cleared"; a.ClearedAt = now;
                a.DurationSec = (int)(now - a.RaisedAt).TotalSeconds;
            }),
            _ => 0
        };
    }

    private async Task<int> RaiseAsync(IngestAlarmRequest req, DateTime plcTs, DateTime now)
    {
        // Re-raise is idempotent-ish: if an identical alarm is already active,
        // don't duplicate the row.
        var exists = await db.Queryable<Alarm>()
            .Where(a => a.PlcAlarmId == req.PlcAlarmId && a.StationNo == req.StationNo
                        && (a.State == "Active" || a.State == "Acked"))
            .AnyAsync();
        if (exists) return 0;

        return await db.Insertable(new Alarm
        {
            PlcAlarmId = req.PlcAlarmId,
            StationNo = req.StationNo,
            Severity = req.Severity,
            State = "Active",
            Text = req.Text,
            RaisedAt = plcTs
        }).ExecuteCommandAsync();
    }

    private async Task<int> TransitionAsync(IngestAlarmRequest req,
        Func<string, bool> stateMatch, Action<Alarm> mutate)
    {
        var alarm = await db.Queryable<Alarm>()
            .Where(a => a.PlcAlarmId == req.PlcAlarmId && a.StationNo == req.StationNo)
            .OrderByDescending(a => a.RaisedAt)
            .FirstAsync();
        if (alarm is null || !stateMatch(alarm.State)) return 0;

        mutate(alarm);
        return await db.Updateable(alarm).ExecuteCommandAsync();
    }

    /// <summary>Operator acknowledges via UI.</summary>
    public async Task<bool> AckAsync(long id)
    {
        var alarm = await GetAsync(id);
        if (alarm is null || alarm.State != "Active") return false;
        alarm.State = "Acked";
        alarm.AckedAt = DateTime.UtcNow;
        return await db.Updateable(alarm).ExecuteCommandAsync() > 0;
    }

    public async Task<int> ActiveCountsAsync()
    {
        return await db.Queryable<Alarm>()
            .Where(a => a.State == "Active" || a.State == "Acked").CountAsync();
    }

    public async Task<(int Active, int Fault)> ActiveAlarmCountsAsync()
    {
        var active = await db.Queryable<Alarm>().Where(a => a.State == "Active" || a.State == "Acked").CountAsync();
        var fault = await db.Queryable<Alarm>().Where(a => a.Severity == 3 && (a.State == "Active" || a.State == "Acked")).CountAsync();
        return (active, fault);
    }
}
