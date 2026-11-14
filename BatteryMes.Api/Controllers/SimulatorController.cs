using BatteryMes.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BatteryMes.Api.Controllers;

/// <summary>Runtime control of the demo PLC simulator (Admin only).</summary>
[ApiController]
[Route("api/simulator")]
[Authorize(Roles = "Admin")]
public class SimulatorController(LineSimulator simulator) : ControllerBase
{
    public record SimulatorState(bool Enabled);

    [HttpGet]
    public IActionResult Get() => Ok(new SimulatorState(simulator.IsRunning));

    [HttpPost("start")]
    public IActionResult Start()
    {
        simulator.SetEnabled(true);
        return Ok(new SimulatorState(true));
    }

    [HttpPost("stop")]
    public IActionResult Stop()
    {
        simulator.SetEnabled(false);
        return Ok(new SimulatorState(false));
    }
}
