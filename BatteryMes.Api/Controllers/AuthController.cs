using System.Security.Claims;
using BatteryMes.Api.Security;
using BatteryMes.Api.Services;
using BatteryMes.Core.DTOs;
using BatteryMes.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace BatteryMes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(ISqlSugarClient db, JwtTokenService jwt) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
    {
        var user = await db.Queryable<User>().Where(u => u.Username == req.Username && u.IsActive).FirstAsync();
        if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { error = "Invalid username or password" });

        var token = jwt.CreateToken(user);
        return Ok(new AuthResponse(token, user.Username, user.Role,
            DateTime.UtcNow.AddMinutes(480)));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!long.TryParse(sub, out var id)) return Unauthorized();
        var user = await db.Queryable<User>().InSingleAsync(id);
        if (user is null) return NotFound();
        return Ok(new UserDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive));
    }
}
