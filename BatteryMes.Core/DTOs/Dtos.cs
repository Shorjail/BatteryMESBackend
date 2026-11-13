namespace BatteryMes.Core.DTOs;

// ---------- Auth ----------
public record LoginRequest(string Username, string Password);
public record AuthResponse(string Token, string Username, string Role, DateTime ExpiresAt);

// ---------- Pagination ----------
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public record PaginationQuery(int Page = 1, int PageSize = 50)
{
    public int Skip => (Math.Max(Page, 1) - 1) * Math.Clamp(PageSize, 1, 500);
}

// ---------- Units ----------
public record UnitDto(
    long Id, string SerialNumber, string? WorkOrderNumber, long? ProductId,
    string Status, int? NgStation, int? NgCode, int CycleTimeMs,
    DateTime EnteredLineAt, DateTime? FinishedAt);

public record UnitDetailDto(
    UnitDto Unit,
    IReadOnlyList<UnitEventDto> Events,
    IReadOnlyList<UnitMaterialDto> Materials,
    IReadOnlyList<UnitTestDto> Tests);

public record UnitEventDto(
    long Id, long UnitId, int StationNo, string EventType, int? StepNo,
    DateTime PlcTimestamp, DateTime MesReceivedAt,
    string? Value, long? OperatorId, string? OperatorName);

public record UnitMaterialDto(
    long Id, string MaterialType, string LotNumber,
    decimal QtyUsed, string? UnitOfMeasure, DateTime ConsumedAt);

public record UnitTestDto(
    long Id, int StationNo, string TestType, decimal MeasuredValue,
    decimal? SpecMin, decimal? SpecMax, string Result, DateTime TestedAt);

public record CreateUnitRequest(string SerialNumber, string? WorkOrderNumber, long? ProductId);
public record UpdateUnitStatusRequest(string Status, int? NgStation, int? NgCode);

// ---------- Ingestion (gateway-facing) ----------
public record IngestEventRequest(
    string UnitSerial,
    int StationNo,
    string EventType,
    int? StepNo,
    DateTime? PlcTimestamp,
    string? Value,
    long? OperatorId,
    int? CycleTimeMs,
    string? IdempotencyKey);

public record IngestBatchRequest(IReadOnlyList<IngestEventRequest> Events);
public record IngestBatchResponse(int Accepted, int Duplicates, int Errors, IReadOnlyList<string> ErrorDetails);

public record IngestAlarmRequest(
    int PlcAlarmId, int StationNo, int Severity, string Text,
    string AlarmAction, // "Raise" | "Ack" | "Clear"
    DateTime? PlcTimestamp);

public record IngestAlarmBatchRequest(IReadOnlyList<IngestAlarmRequest> Alarms);

public record IngestMaterialRequest(
    string UnitSerial, string MaterialType, string LotNumber,
    decimal QtyUsed, string? UnitOfMeasure, DateTime? ConsumedAt);

public record IngestTestRequest(
    string UnitSerial, int StationNo, string TestType,
    decimal MeasuredValue, decimal? SpecMin, decimal? SpecMax,
    DateTime? TestedAt);

// ---------- Alarms ----------
public record AlarmDto(
    long Id, int PlcAlarmId, int StationNo, int Severity, string State,
    string Text, DateTime RaisedAt, DateTime? AckedAt, DateTime? ClearedAt, int? DurationSec);

public record AlarmQuery(int? StationNo, string? State, int? Severity, PaginationQuery Page = default!);
public record AckAlarmRequest(long? OperatorId);

// ---------- Stations ----------
public record StationDto(long Id, int StationNo, string Name, string? ProcessStep,
    int TheoreticalCycleMs, bool IsActive, string? CurrentState);

public record StationStateDto(
    int StationNo, string State, string? CurrentUnitSerial, int? CycleTimeMs,
    long UnitsToday, int UnitsNgToday);

// ---------- Operators / Users / Master data ----------
public record OperatorDto(long Id, string BadgeId, string Name,
    IReadOnlyList<int> StationCertifications, bool IsActive);

public record CreateOperatorRequest(string BadgeId, string Name, IReadOnlyList<int> StationCertifications);
public record UpdateOperatorRequest(string Name, IReadOnlyList<int> StationCertifications, bool IsActive);

public record UserDto(long Id, string Username, string FullName, string Role, bool IsActive);
public record CreateUserRequest(string Username, string Password, string FullName, string Role);

public record ProductDto(long Id, string Code, string Name,
    int? OcvMinMv, int? OcvMaxMv, int? IrMaxMohm,
    int? WeldEnergyMinJ, int? WeldEnergyMaxJ,
    decimal? FillWeightTargetG, decimal? FillWeightTolG,
    decimal? LeakMaxSccm, int? VisionMinScore, bool IsActive);

public record UpsertProductRequest(string Code, string Name,
    int? OcvMinMv, int? OcvMaxMv, int? IrMaxMohm,
    int? WeldEnergyMinJ, int? WeldEnergyMaxJ,
    decimal? FillWeightTargetG, decimal? FillWeightTolG,
    decimal? LeakMaxSccm, int? VisionMinScore, bool IsActive = true);

public record WorkOrderDto(long Id, string WoNumber, long ProductId, string? ProductCode,
    int TargetQty, string Status, DateTime? PlannedStart, DateTime? PlannedEnd, DateTime CreatedAt);

public record CreateWorkOrderRequest(string WoNumber, long ProductId, int TargetQty,
    DateTime? PlannedStart, DateTime? PlannedEnd);

// ---------- Dashboard / OEE / Reports ----------
public record DashboardSummaryDto(
    int UnitsInProgress, int UnitsToday, int UnitsNgToday,
    int ActiveAlarms, int FaultAlarms,
    decimal LineOeePct, DateTime GeneratedAt);

public record StationOeeDto(
    int StationNo, string StationName,
    decimal AvailabilityPct, decimal PerformancePct, decimal QualityPct, decimal OeePct,
    int UnitsProduced, int UnitsNg, int TotalCycleMs);

public record ShiftDto(long Id, DateTime ShiftDate, int ShiftNo, DateTime StartAt, DateTime EndAt);

public record ShiftReportDto(
    long Id, long ShiftId, int StationNo,
    int UnitsProduced, int UnitsNg,
    decimal AvailabilityPct, decimal PerformancePct, decimal QualityPct, decimal OeePct,
    string? DowntimePareto, DateTime GeneratedAt);

public record GenerateShiftReportRequest(long ShiftId);

public record NgParetoItemDto(int NgCode, int NgStation, int Count);

public record LineStatePushRequest(
    int State, string? CurrentUnitSerial, Dictionary<int, string>? StationStates);
