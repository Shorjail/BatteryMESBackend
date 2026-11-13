using SqlSugar;

namespace BatteryMes.Core.Entities;

[SugarTable("users")]
public class User
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64, IsNullable = false)]
    public string Username { get; set; } = string.Empty;

    [SugarColumn(Length = 256, IsNullable = false)]
    public string PasswordHash { get; set; } = string.Empty;

    [SugarColumn(Length = 32, IsNullable = false)]
    public string FullName { get; set; } = string.Empty;

    [SugarColumn(Length = 32, IsNullable = false)]
    public string Role { get; set; } = "Operator";

    [SugarColumn(IsNullable = false)]
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[SugarTable("operators")]
public class Operator
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 32, IsNullable = false)]
    public string BadgeId { get; set; } = string.Empty;

    [SugarColumn(Length = 128, IsNullable = false)]
    public string Name { get; set; } = string.Empty;

    /// <summary>JSON array of station numbers the operator is certified for, e.g. [1,2,3].</summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? StationCertifications { get; set; }

    [SugarColumn(IsNullable = false)]
    public bool IsActive { get; set; } = true;
}

[SugarTable("stations")]
public class Station
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(IsNullable = false)]
    public int StationNo { get; set; }

    [SugarColumn(Length = 128, IsNullable = false)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? ProcessStep { get; set; }

    /// <summary>Theoretical cycle time in ms — used for performance OEE.</summary>
    [SugarColumn(IsNullable = false)]
    public int TheoreticalCycleMs { get; set; }

    [SugarColumn(IsNullable = false)]
    public bool IsActive { get; set; } = true;
}

[SugarTable("products")]
public class Product
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 32, IsNullable = false)]
    public string Code { get; set; } = string.Empty;

    [SugarColumn(Length = 128, IsNullable = false)]
    public string Name { get; set; } = string.Empty;

    // Spec limits — consumed by unit_tests result evaluation.
    public int? OcvMinMv { get; set; }
    public int? OcvMaxMv { get; set; }
    public int? IrMaxMohm { get; set; }
    public int? WeldEnergyMinJ { get; set; }
    public int? WeldEnergyMaxJ { get; set; }
    public decimal? FillWeightTargetG { get; set; }
    public decimal? FillWeightTolG { get; set; }
    public decimal? LeakMaxSccm { get; set; }
    public int? VisionMinScore { get; set; }

    [SugarColumn(IsNullable = false)]
    public bool IsActive { get; set; } = true;
}

[SugarTable("work_orders")]
public class WorkOrder
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 32, IsNullable = false)]
    public string WoNumber { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false)]
    public long ProductId { get; set; }

    [SugarColumn(IsNullable = false)]
    public int TargetQty { get; set; }

    [SugarColumn(Length = 16, IsNullable = false)]
    public string Status { get; set; } = "Planned";

    public DateTime? PlannedStart { get; set; }
    public DateTime? PlannedEnd { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
