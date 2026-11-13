namespace BatteryMes.Core.Enums;

public enum UnitStatus
{
    Pending = 0,
    InProcess = 1,
    Done = 2,
    Ng = 3,
    Scrapped = 4
}

public enum EventType
{
    CarrierIn = 0,
    ProcessStart = 1,
    StepDone = 2,
    QcStart = 3,
    QcPass = 4,
    QcFail = 5,
    Released = 6,
    Done = 7,
    Abort = 8
}

public enum AlarmSeverity
{
    Info = 1,
    Warning = 2,
    Fault = 3
}

public enum AlarmState
{
    Active = 0,
    Acked = 1,
    Cleared = 2
}

public enum WorkOrderStatus
{
    Planned = 0,
    Running = 1,
    Completed = 2,
    Cancelled = 3
}

public enum MaterialType
{
    Pouch = 0,
    Jellyroll = 1,
    Electrolyte = 2,
    Tab = 3,
    SealFilm = 4,
    Label = 5,
    Other = 99
}

public enum UserRole
{
    Operator = 0,
    Supervisor = 1,
    Admin = 2,
    Gateway = 3
}
