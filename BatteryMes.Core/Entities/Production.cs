using SqlSugar;

namespace BatteryMes.Core.Entities;

[SugarTable("units")]
public class Unit
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 32, IsNullable = false)]
    public string SerialNumber { get; set; } = string.Empty;

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? WorkOrderNumber { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? ProductId { get; set; }

    [SugarColumn(Length = 16, IsNullable = false)]
    public string Status { get; set; } = "Pending";

    public int? NgStation { get; set; }
    public int? NgCode { get; set; }

    /// <summary>Total accumulated PLC cycle time across all stations (ms).</summary>
    public int CycleTimeMs { get; set; }

    public DateTime EnteredLineAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [SugarColumn(IsIgnore = true)]
    public List<UnitEvent> Events { get; set; } = new();

    [SugarColumn(IsIgnore = true)]
    public List<UnitMaterial> Materials { get; set; } = new();

    [SugarColumn(IsIgnore = true)]
    public List<UnitTest> Tests { get; set; } = new();
}

[SugarTable("unit_events")]
public class UnitEvent
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(IsNullable = false)]
    public long UnitId { get; set; }

    public int StationNo { get; set; }

    [SugarColumn(Length = 16, IsNullable = false)]
    public string EventType { get; set; } = string.Empty;

    public int? StepNo { get; set; }

    /// <summary>Timestamp reported by the PLC (authoritative for ordering).</summary>
    public DateTime PlcTimestamp { get; set; }

    /// <summary>When the gateway/MES received it — used for latency metrics.</summary>
    public DateTime MesReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Step-specific payload (energy J, weight g, OCV mV…) as JSON text.</summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? Value { get; set; }

    public long? OperatorId { get; set; }

    /// <summary>Dedup key from the edge gateway: (unit,station,step,plc-ts).</summary>
    [SugarColumn(Length = 128, IsNullable = true)]
    public string? IdempotencyKey { get; set; }
}

[SugarTable("unit_materials")]
public class UnitMaterial
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(IsNullable = false)]
    public long UnitId { get; set; }

    [SugarColumn(Length = 32, IsNullable = false)]
    public string MaterialType { get; set; } = "Other";

    [SugarColumn(Length = 32, IsNullable = false)]
    public string LotNumber { get; set; } = string.Empty;

    public decimal QtyUsed { get; set; }
    public string? UnitOfMeasure { get; set; }
    public DateTime ConsumedAt { get; set; } = DateTime.UtcNow;
}

[SugarTable("unit_tests")]
public class UnitTest
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(IsNullable = false)]
    public long UnitId { get; set; }

    public int StationNo { get; set; }

    [SugarColumn(Length = 24, IsNullable = false)]
    public string TestType { get; set; } = string.Empty;

    public decimal MeasuredValue { get; set; }
    public decimal? SpecMin { get; set; }
    public decimal? SpecMax { get; set; }

    [SugarColumn(Length = 4, IsNullable = false)]
    public string Result { get; set; } = "PASS";

    public DateTime TestedAt { get; set; } = DateTime.UtcNow;
}

[SugarTable("alarms")]
public class Alarm
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    public int PlcAlarmId { get; set; }
    public int StationNo { get; set; }
    public int Severity { get; set; } = 1;

    [SugarColumn(Length = 8, IsNullable = false)]
    public string State { get; set; } = "Active";

    [SugarColumn(Length = 80, IsNullable = false)]
    public string Text { get; set; } = string.Empty;

    public DateTime RaisedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AckedAt { get; set; }
    public DateTime? ClearedAt { get; set; }
    public int? DurationSec { get; set; }
}

[SugarTable("shifts")]
public class Shift
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    public DateTime ShiftDate { get; set; }
    public int ShiftNo { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
}

[SugarTable("shift_reports")]
public class ShiftReport
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    public long ShiftId { get; set; }
    public int StationNo { get; set; }

    public int UnitsProduced { get; set; }
    public int UnitsNg { get; set; }
    public decimal AvailabilityPct { get; set; }
    public decimal PerformancePct { get; set; }
    public decimal QualityPct { get; set; }
    public decimal OeePct { get; set; }

    /// <summary>Downtime Pareto as JSON: [{"code":1001,"count":4,"sec":320},…]</summary>
    [SugarColumn(ColumnDataType = "jsonb", IsNullable = true)]
    public string? DowntimePareto { get; set; }

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
