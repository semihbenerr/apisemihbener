using System.ComponentModel.DataAnnotations;
using SemihBener.Plugin.EhliKurban.Data;

namespace SemihBener.Plugin.EhliKurban.Contracts;

public sealed record AnimalRequest(
    [Required, StringLength(32)] string TagNumber,
    [EnumDataType(typeof(AnimalType))] AnimalType Type,
    [Range(0, 5000)] decimal? WeightKg,
    [Range(0, 100_000_000)] decimal Price,
    [Range(1, 7)] int? TotalShares,
    [EnumDataType(typeof(AnimalStatus))] AnimalStatus? Status,
    [StringLength(500)] string? Notes);

public sealed record AnimalDto(
    int Id,
    string TagNumber,
    AnimalType Type,
    decimal? WeightKg,
    decimal Price,
    int TotalShares,
    int TakenShares,
    int FreeShares,
    decimal SharePrice,
    AnimalStatus Status,
    string? Notes,
    DateTime CreatedAtUtc);

public sealed record AnimalDetailDto(AnimalDto Animal, List<CustomerDto> Customers);

public sealed record CustomerRequest(
    [Required, StringLength(128, MinimumLength = 2)] string FullName,
    [Required, Phone, StringLength(20)] string Phone,
    int? AnimalId,
    [Range(1, 7)] int ShareCount,
    [Range(0, 100_000_000)] decimal AmountPaid,
    [StringLength(500)] string? Notes);

public sealed record CustomerDto(
    int Id,
    string FullName,
    string Phone,
    int? AnimalId,
    string? AnimalTagNumber,
    int ShareCount,
    decimal AmountDue,
    decimal AmountPaid,
    decimal Balance,
    string? Notes,
    DateTime CreatedAtUtc);

public sealed record SummaryReport(
    int TotalAnimals,
    int CattleCount,
    int SmallAnimalCount,
    int SlaughteredCount,
    int DeliveredCount,
    int TotalShares,
    int TakenShares,
    int FreeShares,
    int TotalCustomers,
    int UnassignedCustomers,
    decimal TotalAnimalValue,
    decimal AmountDue,
    decimal AmountPaid,
    decimal OutstandingBalance);

public sealed record AnimalReportRow(
    int AnimalId,
    string TagNumber,
    AnimalType Type,
    AnimalStatus Status,
    int TotalShares,
    int TakenShares,
    decimal Price,
    decimal AmountPaid,
    decimal OutstandingBalance);

internal static class EhliKurbanMapping
{
    public static AnimalDto ToDto(this Animal a, int takenShares) => new(
        a.Id, a.TagNumber, a.Type, a.WeightKg, a.Price, a.TotalShares,
        takenShares, Math.Max(0, a.TotalShares - takenShares), a.SharePrice,
        a.Status, a.Notes, a.CreatedAtUtc);

    public static CustomerDto ToDto(this Customer c)
    {
        var due = c.Animal is null ? 0m : c.Animal.SharePrice * c.ShareCount;
        return new CustomerDto(
            c.Id, c.FullName, c.Phone, c.AnimalId, c.Animal?.TagNumber, c.ShareCount,
            due, c.AmountPaid, due - c.AmountPaid, c.Notes, c.CreatedAtUtc);
    }
}
