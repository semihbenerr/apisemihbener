using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SemihBener.Abstractions;
using SemihBener.Plugin.QuizTactics.Contracts;
using SemihBener.Plugin.QuizTactics.Data;

namespace SemihBener.Plugin.QuizTactics.Controllers;

// /api/quiztactics/games
[Route("games")]
public sealed class GamesController(QuizTacticsDbContext db) : PluginControllerBase
{
    [HttpPost]
    public async Task<ActionResult<GameDto>> Submit(SubmitGameRequest request, CancellationToken ct)
    {
        if (request.CorrectAnswers > request.TotalQuestions)
        {
            ModelState.AddModelError(nameof(request.CorrectAnswers), "Doğru cevap sayısı toplam sorudan büyük olamaz.");
            return ValidationProblem(ModelState);
        }

        var userId = CurrentUserId;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return NotFound();
        }

        var game = new Game
        {
            UserId = userId,
            Category = request.Category.Trim(),
            Score = request.Score,
            CorrectAnswers = request.CorrectAnswers,
            TotalQuestions = request.TotalQuestions,
            DurationSeconds = request.DurationSeconds,
            PlayedAtUtc = DateTime.UtcNow
        };

        db.Games.Add(game);
        user.TotalScore += request.Score;
        user.GamesPlayed++;
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = game.Id }, game.ToDto());
    }

    [HttpGet]
    public async Task<ActionResult<List<GameDto>>> MyGames([FromQuery] int take = 20, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        var userId = CurrentUserId;

        var games = await db.Games.AsNoTracking()
            .Where(g => g.UserId == userId)
            .OrderByDescending(g => g.PlayedAtUtc)
            .Take(take)
            .ToListAsync(ct);

        return Ok(games.Select(g => g.ToDto()).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GameDto>> GetById(int id, CancellationToken ct)
    {
        var userId = CurrentUserId;
        var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId, ct);
        return game is null ? NotFound() : Ok(game.ToDto());
    }
}
