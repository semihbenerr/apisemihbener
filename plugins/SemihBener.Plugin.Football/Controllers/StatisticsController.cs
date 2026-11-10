using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SemihBener.Abstractions;
using SemihBener.Plugin.Football.Contracts;
using SemihBener.Plugin.Football.Data;

namespace SemihBener.Plugin.Football.Controllers;

// /api/football/statistics
[Route("statistics")]
public sealed class StatisticsController(FootballDbContext db) : PluginControllerBase
{
    /// <summary>Puan durumu: galibiyet 3, beraberlik 1 puan. Sadece bitmiş maçlar sayılır.</summary>
    [HttpGet("standings")]
    public async Task<ActionResult<List<StandingRow>>> Standings(
        [FromQuery] string league,
        [FromQuery] string season,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(league) || string.IsNullOrWhiteSpace(season))
        {
            ModelState.AddModelError(nameof(league), "league ve season zorunlu.");
            return ValidationProblem(ModelState);
        }

        var matches = await FinishedMatches(league, season).ToListAsync(ct);
        var teams = matches.SelectMany(m => new[] { m.HomeTeam, m.AwayTeam }).Distinct(StringComparer.OrdinalIgnoreCase);

        var rows = teams
            .Select(team => Calculate(team, matches))
            .OrderByDescending(s => s.Points)
            .ThenByDescending(s => s.GoalsFor - s.GoalsAgainst)
            .ThenByDescending(s => s.GoalsFor)
            .ThenBy(s => s.Team, StringComparer.OrdinalIgnoreCase)
            .Select((s, i) => new StandingRow(
                i + 1, s.Team, s.Played, s.Won, s.Drawn, s.Lost,
                s.GoalsFor, s.GoalsAgainst, s.GoalsFor - s.GoalsAgainst, s.Points, s.Form))
            .ToList();

        return Ok(rows);
    }

    [HttpGet("teams/{team}")]
    public async Task<ActionResult<TeamStatsDto>> Team(
        string team,
        [FromQuery] string? league,
        [FromQuery] string? season,
        CancellationToken ct)
    {
        var matches = await FinishedMatches(league, season)
            .Where(m => m.HomeTeam == team || m.AwayTeam == team)
            .ToListAsync(ct);

        if (matches.Count == 0)
        {
            return NotFound();
        }

        var s = Calculate(team, matches);
        var lastMatches = matches.OrderByDescending(m => m.KickoffUtc).Take(5).Select(m => m.ToDto()).ToList();

        return Ok(new TeamStatsDto(
            team, league, season, s.Played, s.Won, s.Drawn, s.Lost, s.GoalsFor, s.GoalsAgainst, s.CleanSheets,
            Math.Round((double)s.GoalsFor / s.Played, 2),
            Math.Round((double)s.GoalsAgainst / s.Played, 2),
            s.Form,
            lastMatches));
    }

    private IQueryable<FootballMatch> FinishedMatches(string? league, string? season)
    {
        var query = db.Matches.AsNoTracking()
            .Where(m => m.Status == MatchStatus.Finished && m.HomeScore != null && m.AwayScore != null);

        if (!string.IsNullOrWhiteSpace(league)) query = query.Where(m => m.League == league);
        if (!string.IsNullOrWhiteSpace(season)) query = query.Where(m => m.Season == season);
        return query;
    }

    private sealed record TeamSummary(
        string Team, int Played, int Won, int Drawn, int Lost,
        int GoalsFor, int GoalsAgainst, int CleanSheets, int Points, string Form);

    private static TeamSummary Calculate(string team, IEnumerable<FootballMatch> allMatches)
    {
        int played = 0, won = 0, drawn = 0, lost = 0, goalsFor = 0, goalsAgainst = 0, cleanSheets = 0;
        var results = new List<(DateTime Kickoff, char Result)>();

        foreach (var m in allMatches)
        {
            var isHome = string.Equals(m.HomeTeam, team, StringComparison.OrdinalIgnoreCase);
            var isAway = string.Equals(m.AwayTeam, team, StringComparison.OrdinalIgnoreCase);
            if (!isHome && !isAway)
            {
                continue;
            }

            var scored = isHome ? m.HomeScore!.Value : m.AwayScore!.Value;
            var conceded = isHome ? m.AwayScore!.Value : m.HomeScore!.Value;

            played++;
            goalsFor += scored;
            goalsAgainst += conceded;
            if (conceded == 0) cleanSheets++;

            char result;
            if (scored > conceded) { won++; result = 'G'; }
            else if (scored == conceded) { drawn++; result = 'B'; }
            else { lost++; result = 'M'; }

            results.Add((m.KickoffUtc, result));
        }

        // Son 5 maç, en yeni en solda (G: galibiyet, B: beraberlik, M: mağlubiyet)
        var form = new string(results.OrderByDescending(r => r.Kickoff).Take(5).Select(r => r.Result).ToArray());
        return new TeamSummary(team, played, won, drawn, lost, goalsFor, goalsAgainst, cleanSheets, won * 3 + drawn, form);
    }
}
