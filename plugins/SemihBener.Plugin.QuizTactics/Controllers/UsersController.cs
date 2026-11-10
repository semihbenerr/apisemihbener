using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SemihBener.Abstractions;
using SemihBener.Plugin.QuizTactics.Contracts;
using SemihBener.Plugin.QuizTactics.Data;

namespace SemihBener.Plugin.QuizTactics.Controllers;

// /api/quiztactics/users
[Route("users")]
public sealed class UsersController(QuizTacticsDbContext db) : PluginControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var userId = CurrentUserId;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? NotFound() : Ok(user.ToDto());
    }

    [HttpGet("leaderboard")]
    public async Task<ActionResult<List<LeaderboardEntry>>> Leaderboard([FromQuery] int take = 20, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);

        var rows = await db.Users.AsNoTracking()
            .OrderByDescending(u => u.TotalScore)
            .ThenBy(u => u.Id)
            .Take(take)
            .Select(u => new { u.Id, u.Username, u.TotalScore, u.GamesPlayed })
            .ToListAsync(ct);

        return Ok(rows.Select((u, i) => new LeaderboardEntry(i + 1, u.Id, u.Username, u.TotalScore, u.GamesPlayed)).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserDto>> GetById(int id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? NotFound() : Ok(user.ToDto());
    }
}
