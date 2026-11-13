using BatteryMes.Api.Security;
using BatteryMes.Core.Entities;
using BatteryMes.Infrastructure.Data;
using SqlSugar;

namespace BatteryMes.Api.Data;

/// <summary>
/// Code-first schema init + seed data. Runs once at startup.
/// Safe to run repeatedly — InitTables is idempotent, seeds check existence.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ISqlSugarClient>();
        var config = services.GetRequiredService<IConfiguration>();

        if (config.GetValue<bool>("Database:AutoInitSchema"))
            SqlSugarConfig.InitializeDatabase(db);

        await SeedAsync(db, config);
    }

    private static async Task SeedAsync(ISqlSugarClient db, IConfiguration config)
    {
        // --- stations (matches PLC stations 1-4) ---
        if (!await db.Queryable<Station>().AnyAsync())
        {
            await db.Insertable(new List<Station>
            {
                new() { StationNo = 1, Name = "Cell Preparation", ProcessStep = "Place pouch/jellyroll, vision check", TheoreticalCycleMs = 8000 },
                new() { StationNo = 2, Name = "Tab Welding", ProcessStep = "Ultrasonic weld + pull test", TheoreticalCycleMs = 6500 },
                new() { StationNo = 3, Name = "Electrolyte Fill & Seal", ProcessStep = "Vacuum fill, seal, leak check", TheoreticalCycleMs = 9000 },
                new() { StationNo = 4, Name = "Test & Pack", ProcessStep = "OCV/IR test, label, pack", TheoreticalCycleMs = 7000 },
            }).ExecuteCommandAsync();
        }

        // --- demo product ---
        if (!await db.Queryable<Product>().AnyAsync())
        {
            await db.Insertable(new Product
            {
                Code = "BAT-POLY-4000",
                Name = "LiPo Pouch 4000mAh",
                OcvMinMv = 3000, OcvMaxMv = 4200, IrMaxMohm = 150,
                WeldEnergyMinJ = 200, WeldEnergyMaxJ = 400,
                FillWeightTargetG = 4.5m, FillWeightTolG = 0.05m,
                LeakMaxSccm = 0.5m, VisionMinScore = 80
            }).ExecuteCommandAsync();
        }

        // --- demo operator ---
        if (!await db.Queryable<Operator>().AnyAsync())
        {
            await db.Insertable(new Operator
            {
                BadgeId = "OP-001", Name = "Demo Operator",
                StationCertifications = "[1,2,3,4]"
            }).ExecuteCommandAsync();
        }

        // --- admin user (password from config Seed:AdminPassword, default "Admin@123") ---
        if (!await db.Queryable<User>().AnyAsync())
        {
            var password = config["Seed:AdminPassword"] ?? "Admin@123";
            await db.Insertable(new User
            {
                Username = "admin",
                PasswordHash = PasswordHasher.Hash(password),
                FullName = "MES Administrator",
                Role = "Admin"
            }).ExecuteCommandAsync();
        }
    }
}
