using SqlSugar;

namespace BatteryMes.Core.Entities;

/// <summary>
/// Periodic snapshot of line/station state pushed by the edge gateway
/// (sourced from PLC DB_MachineState / DB_StationStatus). Used to compute
/// availability for OEE — the PLC owns the truth, MES just samples it.
/// </summary>
[SugarTable("line_state_snapshots")]
public class LineStateSnapshot
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>0 = line-level, 1-4 = station.</summary>
    public int StationNo { get; set; }

    /// <summary>PLC state code: 0=Idle 1=CarrierIn 2=Processing 3=QC 4=Unload 5=Reject 6=Fault.</summary>
    public int State { get; set; }

    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}
