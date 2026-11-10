using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SemihBener.Abstractions;
using SemihBener.Plugin.Football.Contracts;
using SemihBener.Plugin.Football.Data;

namespace SemihBener.Plugin.Football.Controllers;

// /api/football/matches
[Route("matches")]
public sealed class MatchesController(FootballDbContext db) : PluginControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<MatchDto>>> List(
        [FromQuery] string? league,
        [FromQuery] string? season,
        [FromQuery] string? team,
        [FromQuery] MatchStatus? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        var query = db.Matches.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(league)) query = query.Where(m => m.League == league);
        if (!string.IsNullOrWhiteSpace(season)) query = query.Where(m => m.Season == season);
        if (!string.IsNullOrWhiteSpace(team)) query = query.Where(m => m.HomeTeam == team || m.AwayTeam == team);
        if (status is not null) query = query.Where(m => m.Status == status);
        if (from is not null) query = query.Where(m => m.KickoffUtc >= from);
        if (to is not null) query = query.Where(m => m.KickoffUtc <= to);

        var matches = await query.OrderBy(m => m.KickoffUtc).Take(take).ToListAsync(ct);
        return Ok(matches.Select(m => m.ToDto()).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MatchDto>> GetById(int id, CancellationToken ct)
    {
        var match = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        return match is null ? NotFound() : Ok(match.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<MatchDto>> Create(MatchRequest request, CancellationToken ct)
    {
        if (string.Equals(request.HomeTeam.Trim(), request.AwayTeam.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(request.AwayTeam), "Ev sahibi ve deplasman aynı takım olamaz.");
            return ValidationProblem(ModelState);
        }

        var match = new FootballMatch
        {
            League = request.League.Trim(),
            Season = request.Season.Trim(),
            HomeTeam = request.HomeTeam.Trim(),
            AwayTeam = request.AwayTeam.Trim(),
            KickoffUtc = request.KickoffUtc,
            Venue = request.Venue,
            Status = MatchStatus.Scheduled,
            UpdatedAtUtc = DateTime.UtcNow
        };

        db.Matches.Add(match);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = match.Id }, match.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<MatchDto>> Update(int id, MatchRequest request, CancellationToken ct)
    {
        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (match is null)
        {
            return NotFound();
        }

        match.League = request.League.Trim();
        match.Season = request.Season.Trim();
        match.HomeTeam = request.HomeTeam.Trim();
        match.AwayTeam = request.AwayTeam.Trim();
        match.KickoffUtc = request.KickoffUtc;
        match.Venue = request.Venue;
        match.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(match.ToDto());
    }

    [HttpPut("{id:int}/score")]
    public async Task<ActionResult<MatchDto>> UpdateScore(int id, ScoreRequest request, CancellationToken ct)
    {
        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (match is null)
        {
            return NotFound();
        }

        match.HomeScore = request.HomeScore;
        match.AwayScore = request.AwayScore;
        match.Status = request.Status;
        match.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(match.ToDto());
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (match is null)
        {
            return NotFound();
        }

        db.Matches.Remove(match);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
