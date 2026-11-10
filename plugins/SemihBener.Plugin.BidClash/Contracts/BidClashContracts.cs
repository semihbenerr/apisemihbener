using System.ComponentModel.DataAnnotations;
using SemihBener.Plugin.BidClash.Data;

namespace SemihBener.Plugin.BidClash.Contracts;

public sealed record RegisterRequest(
    [Required, StringLength(32, MinimumLength = 3)] string Username,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128, MinimumLength = 8)] string Password);

public sealed record LoginRequest(
    [Required] string UsernameOrEmail,
    [Required] string Password);

public sealed record PlayerDto(int Id, string Username, long Coins, int AuctionsWon, DateTime CreatedAtUtc);

public sealed record AuthResponse(string AccessToken, DateTime ExpiresAtUtc, PlayerDto Player);

public sealed record LeaderboardEntry(int Rank, int PlayerId, string Username, long Coins, int AuctionsWon);

public sealed record CreateAuctionRequest(
    [Required, StringLength(120, MinimumLength = 3)] string Title,
    [StringLength(1000)] string? Description,
    [Range(1d, 1_000_000_000d)] long StartingPrice,
    [Range(1d, 1_000_000d)] long MinIncrement,
    [Range(1, 10_080)] int DurationMinutes);

public sealed record PlaceBidRequest(
    [Range(1d, 1_000_000_000_000d)] long Amount);

public sealed record AuctionDto(
    int Id,
    string Title,
    string? Description,
    long StartingPrice,
    long MinIncrement,
    long CurrentPrice,
    long NextMinimumBid,
    int? HighestBidderId,
    string? HighestBidderName,
    int CreatedById,
    string CreatedByName,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    AuctionStatus Status,
    bool IsOpen,
    int BidCount);

public sealed record BidDto(long Id, int PlayerId, string Username, long Amount, DateTime CreatedAtUtc);

public sealed record AuctionDetailDto(AuctionDto Auction, List<BidDto> RecentBids);

internal static class BidClashMapping
{
    public static PlayerDto ToDto(this Player p) => new(p.Id, p.Username, p.Coins, p.AuctionsWon, p.CreatedAtUtc);
}
