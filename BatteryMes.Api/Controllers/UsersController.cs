using BatteryMes.Api.Security;
using BatteryMes.Core.DTOs;
using BatteryMes.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace BatteryMes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class UsersController(ISqlSugarClient db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> List()
        => Ok((await db.Queryable<User>().OrderBy(u => u.Username).ToListAsync())
            .Select(u => new UserDto(u.Id, u.Username, u.FullName, u.Role, u.IsActive)));

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest req)
    {
        if (await db.Queryable<User>().AnyAsync(u => u.Username == req.Username))
            return Conflict(new { error = "Username already exists" });
        var user = new User
        {
            Username = req.Username,
            PasswordHash = PasswordHasher.Hash(req.Password),
            FullName = req.FullName,
            Role = req.Role
        };
        user.Id = await db.Insertable(user).ExecuteReturnBigIdentityAsync();
        return CreatedAtAction(nameof(List),
            new UserDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive));
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, CreateUserRequest req)
    {
        var user = await db.Queryable<User>().InSingleAsync(id);
        if (user is null) return NotFound();
        user.FullName = req.FullName;
        user.Role = req.Role;
        if (!string.IsNullOrEmpty(req.Password))
            user.PasswordHash = PasswordHasher.Hash(req.Password);
        await db.Updateable(user).ExecuteCommandAsync();
        return NoContent();
    }
}
