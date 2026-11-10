using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SemihBener.Abstractions;
using SemihBener.Plugin.EhliKurban.Contracts;
using SemihBener.Plugin.EhliKurban.Data;

namespace SemihBener.Plugin.EhliKurban.Controllers;

// /api/ehlikurban/reports
[Route("reports")]
public sealed class ReportsController(EhliKurbanDbContext db) : PluginControllerBase
{
    // GET /api/ehlikurban/reports/summary
    [HttpGet("summary")]
    public async Task<ActionResult<SummaryReport>> Summary(CancellationToken ct)
    {
        var animals = await db.Animals.AsNoTracking().Include(a => a.Customers).ToListAsync(ct);
        var totalCustomers = await db.Customers.CountAsync(ct);
        var unassigned = await db.Customers.CountAsync(c => c.AnimalId == null, ct);

        var totalShares = animals.Sum(a => a.TotalShares);
        var takenShares = animals.Sum(a => a.Customers.Sum(c => c.ShareCount));
        var amountDue = animals.Sum(a => a.Customers.Sum(c => a.SharePrice * c.ShareCount));
        var amountPaid = await db.Customers.SumAsync(c => c.AmountPaid, ct);

        return Ok(new SummaryReport(
            TotalAnimals: animals.Count,
            CattleCount: animals.Count(a => a.Type == AnimalType.Cattle),
            SmallAnimalCount: animals.Count(a => a.Type != AnimalType.Cattle),
            SlaughteredCount: animals.Count(a => a.Status == AnimalStatus.Slaughtered),
            DeliveredCount: animals.Count(a => a.Status == AnimalStatus.Delivered),
            TotalShares: totalShares,
            TakenShares: takenShares,
            FreeShares: totalShares - takenShares,
            TotalCustomers: totalCustomers,
            UnassignedCustomers: unassigned,
            TotalAnimalValue: animals.Sum(a => a.Price),
            AmountDue: amountDue,
            AmountPaid: amountPaid,
            OutstandingBalance: amountDue - amountPaid));
    }

    // GET /api/ehlikurban/reports/animals
    [HttpGet("animals")]
    public async Task<ActionResult<List<AnimalReportRow>>> Animals(CancellationToken ct)
    {
        var animals = await db.Animals.AsNoTracking().Include(a => a.Customers).OrderBy(a => a.TagNumber).ToListAsync(ct);

        var rows = animals.Select(a =>
        {
            var taken = a.Customers.Sum(c => c.ShareCount);
            var due = a.Customers.Sum(c => a.SharePrice * c.ShareCount);
            var paid = a.Customers.Sum(c => c.AmountPaid);
            return new AnimalReportRow(a.Id, a.TagNumber, a.Type, a.Status, a.TotalShares, taken, a.Price, paid, due - paid);
        }).ToList();

        return Ok(rows);
    }

    // GET /api/ehlikurban/reports/debtors
    [HttpGet("debtors")]
    public async Task<ActionResult<List<CustomerDto>>> Debtors(CancellationToken ct)
    {
        var customers = await db.Customers.AsNoTracking()
            .Include(c => c.Animal)
            .Where(c => c.AnimalId != null)
            .ToListAsync(ct);

        return Ok(customers
            .Select(c => c.ToDto())
            .Where(c => c.Balance > 0)
            .OrderByDescending(c => c.Balance)
            .ToList());
    }
}
