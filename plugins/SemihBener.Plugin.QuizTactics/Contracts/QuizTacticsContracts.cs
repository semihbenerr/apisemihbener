using System.ComponentModel.DataAnnotations;
using SemihBener.Plugin.QuizTactics.Data;

namespace SemihBener.Plugin.QuizTactics.Contracts;

public sealed record RegisterRequest(
    [Required, StringLength(32, MinimumLength = 3)] string Username,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128, MinimumLength = 8)] string Password);

public sealed record LoginRequest(
    [Required] string UsernameOrEmail,
    [Required] string Password);

public sealed record UserDto(int Id, string Username, int TotalScore, int GamesPlayed, DateTime CreatedAtUtc);

public sealed record AuthResponse(string AccessToken, DateTime ExpiresAtUtc, UserDto User);

public sealed record LeaderboardEntry(int Rank, int UserId, string Username, int TotalScore, int GamesPlayed);

public sealed record SubmitGameRequest(
    [Required, StringLength(64)] string Category,
    [Range(0, 1_000_000)] int Score,
    [Range(0, 1000)] int CorrectAnswers,
    [Range(1, 1000)] int TotalQuestions,
    [Range(0, 86_400)] int DurationSeconds);

public sealed record GameDto(
    int Id,
    string Category,
    int Score,
    int CorrectAnswers,
    int TotalQuestions,
    int DurationSeconds,
    DateTime PlayedAtUtc);

internal static class QuizTacticsMapping
{
    public static UserDto ToDto(this QuizUser u) =>
        new(u.Id, u.Username, u.TotalScore, u.GamesPlayed, u.CreatedAtUtc);

    public static GameDto ToDto(this Game g) =>
        new(g.Id, g.Category, g.Score, g.CorrectAnswers, g.TotalQuestions, g.DurationSeconds, g.PlayedAtUtc);
}
