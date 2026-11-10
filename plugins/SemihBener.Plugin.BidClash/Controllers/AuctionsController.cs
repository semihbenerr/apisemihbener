using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SemihBener.Abstractions;
using SemihBener.Plugin.BidClash.Contracts;
using SemihBener.Plugin.BidClash.Data;

namespace SemihBener.Plugin.BidClash.Controllers;

// /api/bidclash/auctions
[Route("auctions")]
public sealed class AuctionsController(BidClashDbContext db) : PluginControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AuctionDto>>> List(
        [FromQuery] AuctionStatus status = AuctionStatus.Active,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        var now = DateTime.UtcNow;

        var query = db.Auctions.AsNoTracking().Where(a => a.Status == status);
        query = status == AuctionStatus.Active
            ? query.Where(a => a.EndsAtUtc > now).OrderBy(a => a.EndsAtUtc)
            : query.OrderByDescending(a => a.EndsAtUtc);

        return Ok(await Project(query.Take(take), now).ToListAsync(ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AuctionDetailDto>> GetById(int id, CancellationToken ct)
    {
        var dto = await LoadDtoAsync(id, ct);
        if (dto is null)
        {
            return NotFound();
        }

        var bids = await db.Bids.AsNoTracking()
            .Where(b => b.AuctionId == id)
            .OrderByDescending(b => b.Amount)
            .Take(20)
            .Select(b => new BidDto(b.Id, b.PlayerId, b.Player.Username, b.Amount, b.CreatedAtUtc))
            .ToListAsync(ct);

        return Ok(new AuctionDetailDto(dto, bids));
    }

    [HttpPost]
    public async Task<ActionResult<AuctionDto>> Create(CreateAuctionRequest request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var auction = new Auction
        {
            Title = request.Title.Trim(),
            Description = request.Description,
            StartingPrice = request.StartingPrice,
            MinIncrement = request.MinIncrement,
            CurrentPrice = request.StartingPrice,
            CreatedById = CurrentUserId,
            StartsAtUtc = now,
            EndsAtUtc = now.AddMinutes(request.DurationMinutes),
            Status = AuctionStatus.Active
        };

        db.Auctions.Add(auction);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = auction.Id }, await LoadDtoAsync(auction.Id, ct));
    }

    [HttpPost("{id:int}/bids")]
    public async Task<ActionResult<AuctionDto>> PlaceBid(int id, PlaceBidRequest request, CancellationToken ct)
    {
        var playerId = CurrentUserId;
        var now = DateTime.UtcNow;

        var auction = await db.Auctions.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (auction is null)
        {
            return NotFound();
        }

        if (auction.Status != AuctionStatus.Active || auction.EndsAtUtc <= now)
        {
            return Problem(title: "Açık artırma kapandı.", statusCode: StatusCodes.Status409Conflict);
        }

        if (auction.CreatedById == playerId)
        {
            return Problem(title: "Kendi açık artırmana teklif veremezsin.", statusCode: StatusCodes.Status409Conflict);
        }

        if (auction.HighestBidderId == playerId)
        {
            return Problem(title: "En yüksek teklif zaten sende.", statusCode: StatusCodes.Status409Conflict);
        }

        var minimum = auction.HighestBidderId is null
            ? auction.StartingPrice
            : auction.CurrentPrice + auction.MinIncrement;

        if (request.Amount < minimum)
        {
            ModelState.AddModelError(nameof(request.Amount), $"Teklif en az {minimum} olmalı.");
            return ValidationProblem(ModelState);
        }

        var coins = await db.Players.Where(p => p.Id == playerId).Select(p => (long?)p.Coins).FirstOrDefaultAsync(ct);
        if (coins is null)
        {
            return Unauthorized();
        }

        if (coins < request.Amount)
        {
            return Problem(title: $"Yetersiz coin. Bakiyen: {coins}.", statusCode: StatusCodes.Status409Conflict);
        }

        auction.CurrentPrice = request.Amount;
        auction.HighestBidderId = playerId;
        auction.Version++;
        db.Bids.Add(new Bid { AuctionId = id, PlayerId = playerId, Amount = request.Amount, CreatedAtUtc = now });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(title: "Bu sırada daha yüksek bir teklif geldi, tekrar dene.", statusCode: StatusCodes.Status409Conflict);
        }

        return Ok(await LoadDtoAsync(id, ct));
    }

    /// <summary>Süresi dolan açık artırmayı herkes kapatabilir; süre dolmadan sadece oluşturan kapatabilir.</summary>
    [HttpPost("{id:int}/finish")]
    public async Task<ActionResult<AuctionDto>> Finish(int id, CancellationToken ct)
    {
        var playerId = CurrentUserId;
        var now = DateTime.UtcNow;

        var auction = await db.Auctions.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (auction is null)
        {
            return NotFound();
        }

        if (auction.Status != AuctionStatus.Active)
        {
            return Problem(title: "Açık artırma zaten kapatılmış.", statusCode: StatusCodes.Status409Conflict);
        }

        if (auction.EndsAtUtc > now && auction.CreatedById != playerId)
        {
            return Problem(title: "Süre dolmadan yalnızca oluşturan kişi kapatabilir.", statusCode: StatusCodes.Status403Forbidden);
        }

        auction.Status = AuctionStatus.Finished;
        auction.Version++;
        if (auction.EndsAtUtc > now)
        {
            auction.EndsAtUtc = now;
        }

        if (auction.HighestBidderId is int winnerId)
        {
            var winner = await db.Players.FirstAsync(p => p.Id == winnerId, ct);
            var seller = await db.Players.FirstAsync(p => p.Id == auction.CreatedById, ct);

            var paid = Math.Min(winner.Coins, auction.CurrentPrice);
            winner.Coins -= paid;
            winner.AuctionsWon++;
            winner.Version++;
            seller.Coins += paid;
            seller.Version++;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(title: "Açık artırma bu sırada güncellendi, tekrar dene.", statusCode: StatusCodes.Status409Conflict);
        }

        return Ok(await LoadDtoAsync(id, ct));
    }

    private Task<AuctionDto?> LoadDtoAsync(int id, CancellationToken ct) =>
        Project(db.Auctions.AsNoTracking().Where(a => a.Id == id), DateTime.UtcNow).FirstOrDefaultAsync(ct);

    private static IQueryable<AuctionDto> Project(IQueryable<Auction> query, DateTime now) =>
        query.Select(a => new AuctionDto(
            a.Id,
            a.Title,
            a.Description,
            a.StartingPrice,
            a.MinIncrement,
            a.CurrentPrice,
            a.HighestBidderId == null ? a.StartingPrice : a.CurrentPrice + a.MinIncrement,
            a.HighestBidderId,
            a.HighestBidder != null ? a.HighestBidder.Username : null,
            a.CreatedById,
            a.CreatedBy.Username,
            a.StartsAtUtc,
            a.EndsAtUtc,
            a.Status,
            a.Status == AuctionStatus.Active && a.EndsAtUtc > now,
            a.Bids.Count));
}
