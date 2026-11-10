using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SemihBener.Abstractions;
using SemihBener.Plugin.QuizTactics.Contracts;
using SemihBener.Plugin.QuizTactics.Data;

namespace SemihBener.Plugin.QuizTactics.Controllers;

// /api/quiztactics/auth
[Route("auth")]
[AllowAnonymous]
[StrictRateLimit]
public sealed class AuthController(
    QuizTacticsDbContext db,
    JwtTokenIssuer<QuizTacticsPlugin> tokens) : PluginControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.Username == username || u.Email == email, ct))
        {
            return Problem(title: "Kullanıcı adı veya e-posta zaten kayıtlı.", statusCode: StatusCodes.Status409Conflict);
        }

        var user = new QuizUser
        {
            Username = username,
            Email = email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return Ok(CreateResponse(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var login = request.UsernameOrEmail.Trim();
        var loginEmail = login.ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == login || u.Email == loginEmail, ct);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Problem(title: "Kullanıcı adı veya şifre hatalı.", statusCode: StatusCodes.Status401Unauthorized);
        }

        return Ok(CreateResponse(user));
    }

    private AuthResponse CreateResponse(QuizUser user)
    {
        var token = tokens.CreateToken(user.Id, user.Username);
        return new AuthResponse(token.AccessToken, token.ExpiresAtUtc, user.ToDto());
    }
}
