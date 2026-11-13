using BatteryMes.Core.Entities;
using Microsoft.Extensions.Configuration;
using SqlSugar;

namespace BatteryMes.Infrastructure.Data;

public class MesDbOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "BatteryMes";
    public string Username { get; set; } = "postgres";
    public string Password { get; set; } = string.Empty;
    public bool AutoInitSchema { get; set; } = true;
}

public static class SqlSugarConfig
{
    public static ISqlSugarClient CreateClient(IConfiguration config)
    {
        var d = new MesDbOptions();
        var opt = new MesDbOptions
        {
            Host = config["Database:Host"] ?? d.Host,
            Port = int.TryParse(config["Database:Port"], out var p) ? p : d.Port,
            Database = config["Database:Database"] ?? d.Database,
            Username = config["Database:Username"] ?? d.Username,
            Password = config["Database:Password"] ?? d.Password,
            AutoInitSchema = !bool.TryParse(config["Database:AutoInitSchema"], out var a) || a
        };

        var connectionString =
            $"Host={opt.Host};Port={opt.Port};Database={opt.Database};" +
            $"Username={opt.Username};Password={opt.Password};" +
            "Include Error Detail=true;Trust Server Certificate=true;";

        return new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.PostgreSQL,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityService = (property, column) =>
                {
                    // Snake_case column names, but respect explicit ColumnName.
                    if (column.DbColumnName == property.Name)
                        column.DbColumnName = ToSnakeCase(property.Name);

                    // Nullable<T> value types must map to nullable columns —
                    // SqlSugar's code-first otherwise emits NOT NULL.
                    if (Nullable.GetUnderlyingType(property.PropertyType) != null)
                        column.IsNullable = true;
                }
            },
            MoreSettings = new ConnMoreSettings
            {
                IsAutoRemoveDataCache = true,
                IsAutoDeleteQueryFilter = true,
                IsAutoUpdateQueryFilter = true
            }
        });
    }

    private static string ToSnakeCase(string name)
    {
        // PascalCase / camelCase → snake_case; leave digits & existing
        // underscores intact; collapse acronym runs (e.g. "OCV" → "ocv").
        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var prevLower = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
                var nextLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                var acronymEnd = char.IsUpper(c) && i > 0 && char.IsUpper(name[i - 1]) && nextLower;
                if ((prevLower || acronymEnd) && sb.Length > 0 && sb[^1] != '_')
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public static void InitializeDatabase(ISqlSugarClient db)
    {
        db.CodeFirst.InitTables(
            typeof(User),
            typeof(Operator),
            typeof(Station),
            typeof(Product),
            typeof(WorkOrder),
            typeof(Unit),
            typeof(UnitEvent),
            typeof(UnitMaterial),
            typeof(UnitTest),
            typeof(Alarm),
            typeof(Shift),
            typeof(ShiftReport),
            typeof(LineStateSnapshot));

        // Indexes that code-first doesn't express declaratively.
        db.Ado.ExecuteCommand(
            "CREATE UNIQUE INDEX IF NOT EXISTS ux_unit_events_idempotency " +
            "ON unit_events (idempotency_key) WHERE idempotency_key IS NOT NULL;");
        db.Ado.ExecuteCommand(
            "CREATE INDEX IF NOT EXISTS ix_unit_events_unit_ts " +
            "ON unit_events (unit_id, plc_timestamp);");
        db.Ado.ExecuteCommand(
            "CREATE INDEX IF NOT EXISTS ix_unit_events_station_type_ts " +
            "ON unit_events (station_no, event_type, plc_timestamp);");
        db.Ado.ExecuteCommand(
            "CREATE INDEX IF NOT EXISTS ix_alarms_state " +
            "ON alarms (state, raised_at);");
        db.Ado.ExecuteCommand(
            "CREATE INDEX IF NOT EXISTS ix_units_status " +
            "ON units (status, entered_line_at);");
        db.Ado.ExecuteCommand(
            "CREATE INDEX IF NOT EXISTS ix_unit_tests_unit " +
            "ON unit_tests (unit_id, test_type);");
    }
}
