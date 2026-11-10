using System.ComponentModel.DataAnnotations;
using SemihBener.Plugin.Football.Data;

namespace SemihBener.Plugin.Football.Contracts;

public sealed record MatchRequest(
    [Required, StringLength(64)] string League,
    [Required, StringLength(16)] string Season,
    [Required, StringLength(64)] string HomeTeam,
    [Required, StringLength(64)] string AwayTeam,
    DateTime KickoffUtc,
    [StringLength(128)] string? Venue);

public sealed record ScoreRequest(
    [Range(0, 99)] int HomeScore,
    [Range(0, 99)] int AwayScore,
    [EnumDataType(typeof(MatchStatus))] MatchStatus Status);

public sealed record MatchDto(
    int Id,
    string League,
    string Season,
    string HomeTeam,
    string AwayTeam,
    int? HomeScore,
    int? AwayScore,
    DateTime KickoffUtc,
    MatchStatus Status,
    string? Venue,
    DateTime UpdatedAtUtc);

public sealed record StandingRow(
    int Rank,
    string Team,
    int Played,
    int Won,
    int Drawn,
    int Lost,
    int GoalsFor,
    int GoalsAgainst,
    int GoalDifference,
    int Points,
    string Form);

public sealed record TeamStatsDto(
    string Team,
    string? League,
    string? Season,
    int Played,
    int Won,
    int Drawn,
    int Lost,
    int GoalsFor,
    int GoalsAgainst,
    int CleanSheets,
    double AvgGoalsFor,
    double AvgGoalsAgainst,
    string Form,
    List<MatchDto> LastMatches);

internal static class FootballMapping
{
    public static MatchDto ToDto(this FootballMatch m) => new(
        m.Id, m.League, m.Season, m.HomeTeam, m.AwayTeam, m.HomeScore, m.AwayScore,
        m.KickoffUtc, m.Status, m.Venue, m.UpdatedAtUtc);
}
