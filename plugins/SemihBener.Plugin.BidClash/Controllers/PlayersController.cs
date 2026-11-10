using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SemihBener.Abstractions;
using SemihBener.Plugin.BidClash.Contracts;
using SemihBener.Plugin.BidClash.Data;

namespace SemihBener.Plugin.BidClash.Controllers;

// /api/bidclash/players
[Route("players")]
public sealed class PlayersController(BidClashDbContext db) : PluginControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<PlayerDto>> Me(CancellationToken ct)
    {
        var playerId = CurrentUserId;
        var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playerId, ct);
        return player is null ? NotFound() : Ok(player.ToDto());
    }

    [HttpGet("leaderboard")]
    public async Task<ActionResult<List<LeaderboardEntry>>> Leaderboard([FromQuery] int take = 20, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);

        var rows = await db.Players.AsNoTracking()
            .OrderByDescending(p => p.Coins)
            .ThenByDescending(p => p.AuctionsWon)
            .ThenBy(p => p.Id)
            .Take(take)
            .Select(p => new { p.Id, p.Username, p.Coins, p.AuctionsWon })
            .ToListAsync(ct);

        return Ok(rows.Select((p, i) => new LeaderboardEntry(i + 1, p.Id, p.Username, p.Coins, p.AuctionsWon)).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PlayerDto>> GetById(int id, CancellationToken ct)
    {
        var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return player is null ? NotFound() : Ok(player.ToDto());
    }
}
