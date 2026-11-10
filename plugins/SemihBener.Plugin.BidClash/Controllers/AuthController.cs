using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SemihBener.Abstractions;
using SemihBener.Plugin.BidClash.Contracts;
using SemihBener.Plugin.BidClash.Data;

namespace SemihBener.Plugin.BidClash.Controllers;

// /api/bidclash/auth
[Route("auth")]
[AllowAnonymous]
[StrictRateLimit]
public sealed class AuthController(
    BidClashDbContext db,
    JwtTokenIssuer<BidClashPlugin> tokens) : PluginControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        if (await db.Players.AnyAsync(p => p.Username == username || p.Email == email, ct))
        {
            return Problem(title: "Kullanıcı adı veya e-posta zaten kayıtlı.", statusCode: StatusCodes.Status409Conflict);
        }

        var player = new Player
        {
            Username = username,
            Email = email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            Coins = Player.StartingCoins,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Players.Add(player);
        await db.SaveChangesAsync(ct);

        return Ok(CreateResponse(player));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var login = request.UsernameOrEmail.Trim();
        var loginEmail = login.ToLowerInvariant();

        var player = await db.Players.FirstOrDefaultAsync(p => p.Username == login || p.Email == loginEmail, ct);
        if (player is null || !PasswordHasher.Verify(request.Password, player.PasswordHash))
        {
            return Problem(title: "Kullanıcı adı veya şifre hatalı.", statusCode: StatusCodes.Status401Unauthorized);
        }

        return Ok(CreateResponse(player));
    }

    private AuthResponse CreateResponse(Player player)
    {
        var token = tokens.CreateToken(player.Id, player.Username);
        return new AuthResponse(token.AccessToken, token.ExpiresAtUtc, player.ToDto());
    }
}
