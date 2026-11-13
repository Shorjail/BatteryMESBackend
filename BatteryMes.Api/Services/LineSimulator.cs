using BatteryMes.Core.DTOs;
using BatteryMes.Core.Entities;
using BatteryMes.Core.Enums;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BatteryMes.Api.Services;

public class SimulatorOptions
{
    public bool Enabled { get; set; }
    /// <summary>Average seconds between new units entering the line.</summary>
    public double SpawnIntervalSec { get; set; } = 20;
    /// <summary>Chance a unit fails QC at a station (0-100).</summary>
    public int NgRatePct { get; set; } = 5;
    /// <summary>Emit periodic line-state snapshots for OEE availability.</summary>
    public bool EmitLineState { get; set; } = true;
}

/// <summary>
/// Demo data generator — mimics the S7 PLC output described in
/// docs/04-plc-tags.md by driving the same ingestion path a real edge
/// gateway would use. Enabled only in Development via Simulator:Enabled.
/// </summary>
public class LineSimulator(
    IServiceScopeFactory scopeFactory,
    IOptions<SimulatorOptions> options,
    ILogger<LineSimulator> logger) : BackgroundService
{
    private readonly SimulatorOptions _opt = options.Value;
    private readonly Random _rng = new();
    // Unique per process — serials must never collide with existing units,
    // otherwise events attach to the wrong unit row.
    private readonly string _instanceTag = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
    private int _serialSeed;

    // Pending work: (dueUtc, action). Units flow STN1→STN4.
    private readonly List<(DateTime Due, Func<CancellationToken, Task> Action)> _pending = new();
    private readonly Dictionary<long, string> _unitSerials = new();
    private long _unitKey;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opt.Enabled)
        {
            logger.LogInformation("LineSimulator disabled (Simulator:Enabled=false)");
            return;
        }
        logger.LogInformation("LineSimulator started — spawning unit every ~{Sec}s", _opt.SpawnIntervalSec);

        // Abandoned units from a previous run would sit InProcess forever and
        // block station occupancy — scrap them on startup.
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var n = await db.Updateable<Core.Entities.Unit>()
                .SetColumns(u => u.Status == "Scrapped")
                .Where(u => u.Status == "InProcess")
                .ExecuteCommandAsync();
            if (n > 0) logger.LogInformation("LineSimulator scrapped {Count} abandoned in-process units", n);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Simulator stale-unit cleanup failed"); }

        var nextSpawn = DateTime.UtcNow;
        var nextLineState = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            if (now >= nextSpawn)
            {
                SpawnUnit(now);
                nextSpawn = now.AddSeconds(_opt.SpawnIntervalSec * (0.6 + _rng.NextDouble() * 0.8));
            }

            // Emit due work
            var due = _pending.Where(p => p.Due <= now).ToList();
            foreach (var p in due)
            {
                _pending.Remove(p);
                await p.Action(stoppingToken);
            }

            if (_opt.EmitLineState && now >= nextLineState)
            {
                await EmitLineStateAsync(stoppingToken);
                nextLineState = now.AddSeconds(10);
            }

            await Task.Delay(1000, stoppingToken);
        }
    }

    private void SpawnUnit(DateTime now)
    {
        var key = Interlocked.Increment(ref _unitKey);
        var serial = $"SN-{DateTime.UtcNow:yyMMdd}-{_instanceTag}-{Interlocked.Increment(ref _serialSeed)}";
        _unitSerials[key] = serial;

        // Decide NG station (or none). Stations: 1=Prep 2=Weld 3=Fill/Seal 4=Test.
        int? ngStation = _rng.Next(100) < _opt.NgRatePct
            ? _rng.Next(1, 5)
            : null;

        var t = now;
        var step = 0;
        for (var st = 1; st <= 4; st++)
        {
            // transit gap grows slightly downstream (buffers between stations)
            t = t.AddSeconds(st == 1 ? 0 : 4 + _rng.Next(0, 5));

            Enqueue(key, t, Ev(serial, st, "CarrierIn", null, null, 0));
            t = t.AddMilliseconds(400);

            // process steps: 3-5 per station
            var steps = 3 + _rng.Next(0, 3);
            for (var s = 1; s <= steps; s++)
            {
                t = t.AddMilliseconds(1200 + _rng.Next(0, 1500));
                var value = st switch
                {
                    2 => NgJson(_rng.Next(180, 420), "weldEnergyJ"),
                    3 => NgJson(Math.Round(4.2 + _rng.NextDouble() * 0.6, 2), "fillWeightG"),
                    4 => NgJson(_rng.Next(3000, 4100), "ocvMv"),
                    _ => null
                };
                Enqueue(key, t, Ev(serial, st, "StepDone", s, value, CycleFor(st)));
            }

            t = t.AddMilliseconds(600);
            Enqueue(key, t, Ev(serial, st, "QcStart", null, null, 0));
            t = t.AddMilliseconds(800);

            var failed = ngStation == st;
            if (failed)
            {
                Enqueue(key, t, Ev(serial, st, "QcFail", 1, NgJson(st, "reason"), 0));
                // unit scrapped — no further stations
                _unitSerials.Remove(key);
                break;
            }

            Enqueue(key, t, Ev(serial, st, "QcPass", null, null, 0));
            t = t.AddMilliseconds(500);
            Enqueue(key, t, Ev(serial, st, "Released", null, null, 0));

            if (st == 4)
            {
                t = t.AddMilliseconds(300);
                Enqueue(key, t, Ev(serial, st, "Done", null, null, 0));
                _unitSerials.Remove(key);
            }
        }

        // Occasionally inject a test measurement + material genealogy
        if (_rng.Next(100) < 80)
            EnqueueMaterial(key, now.AddSeconds(30), new IngestMaterialRequest(
                serial, "Jellyroll", $"LOT-{_rng.Next(1000, 9999)}", 1m, "pcs", null));
        if (_rng.Next(100) < 70)
            EnqueueTest(key, now.AddSeconds(45), new IngestTestRequest(
                serial, 4, "OCV", _rng.Next(3000, 4100), 3000, 4200, null));
    }

    private void Enqueue(long key, DateTime due, IngestEventRequest ev)
    {
        _pending.Add((due, ct => EmitEventAsync(ev, ct)));
    }

    private void EnqueueMaterial(long key, DateTime due, IngestMaterialRequest req) =>
        _pending.Add((due, ct => EmitMaterialAsync(req, ct)));

    private void EnqueueTest(long key, DateTime due, IngestTestRequest req) =>
        _pending.Add((due, ct => EmitTestAsync(req, ct)));

    private IngestEventRequest Ev(string serial, int station, string type,
        int? step, string? value, int cycleMs) => new(
        serial, station, type, step,
        DateTime.UtcNow, value, null, cycleMs > 0 ? cycleMs : null,
        $"sim-{Guid.NewGuid():N}");

    private static string NgJson(object v, string prop) =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { [prop] = v });

    private int CycleFor(int station) => station switch
    {
        1 => 6000 + _rng.Next(0, 3000),
        2 => 5000 + _rng.Next(0, 2500),
        3 => 7000 + _rng.Next(0, 3000),
        _ => 5500 + _rng.Next(0, 2500)
    };

    private async Task EmitEventAsync(IngestEventRequest ev, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<UnitService>();
            // Stamp at emit time so PLC timestamps reflect real flow order.
            var stamped = ev with { PlcTimestamp = DateTime.UtcNow };
            await svc.IngestEventsAsync(new IngestBatchRequest([stamped]));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Simulator event emit failed for {Serial}", ev.UnitSerial);
        }
        await Task.Delay(50, ct);
    }

    private async Task EmitMaterialAsync(IngestMaterialRequest req, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<UnitService>().AddMaterialAsync(req);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Simulator material emit failed"); }
        await Task.Delay(30, ct);
    }

    private async Task EmitTestAsync(IngestTestRequest req, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<UnitService>().AddTestAsync(req);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Simulator test emit failed"); }
        await Task.Delay(30, ct);
    }

    private async Task EmitLineStateAsync(CancellationToken ct)
    {
        // Stations mostly "Processing" with occasional Idle — feeds OEE availability.
        var states = Enumerable.Range(1, 4)
            .ToDictionary(i => i, i => _rng.Next(100) < 75 ? "Processing" : "Idle");
        try
        {
            using var scope = scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<OeeService>();
            await svc.RecordLineStateAsync(new LineStatePushRequest(1, null, states));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Simulator line-state emit failed");
        }
    }
}
