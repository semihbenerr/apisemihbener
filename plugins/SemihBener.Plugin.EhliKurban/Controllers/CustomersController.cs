using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SemihBener.Abstractions;
using SemihBener.Plugin.EhliKurban.Contracts;
using SemihBener.Plugin.EhliKurban.Data;

namespace SemihBener.Plugin.EhliKurban.Controllers;

// /api/ehlikurban/customers
[Route("customers")]
public sealed class CustomersController(EhliKurbanDbContext db) : PluginControllerBase
{
    // GET /api/ehlikurban/customers?animalId=3&search=ahmet
    [HttpGet]
    public async Task<ActionResult<List<CustomerDto>>> List(
        [FromQuery] int? animalId,
        [FromQuery] string? search,
        [FromQuery] bool onlyWithBalance = false,
        CancellationToken ct = default)
    {
        var query = db.Customers.AsNoTracking().Include(c => c.Animal).AsQueryable();

        if (animalId is not null) query = query.Where(c => c.AnimalId == animalId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c => c.FullName.Contains(term) || c.Phone.Contains(term));
        }

        var customers = await query.OrderBy(c => c.FullName).ToListAsync(ct);
        var result = customers.Select(c => c.ToDto());
        if (onlyWithBalance) result = result.Where(c => c.Balance > 0);

        return Ok(result.ToList());
    }

    // GET /api/ehlikurban/customers/8
    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomerDto>> GetById(int id, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().Include(c => c.Animal).FirstOrDefaultAsync(c => c.Id == id, ct);
        return customer is null ? NotFound() : Ok(customer.ToDto());
    }

    // POST /api/ehlikurban/customers
    [HttpPost]
    public async Task<ActionResult<CustomerDto>> Create(CustomerRequest request, CancellationToken ct)
    {
        var capacityError = await CheckCapacityAsync(request.AnimalId, request.ShareCount, excludeCustomerId: null, ct);
        if (capacityError is not null) return capacityError;

        var customer = new Customer
        {
            FullName = request.FullName.Trim(),
            Phone = request.Phone.Trim(),
            AnimalId = request.AnimalId,
            ShareCount = request.ShareCount,
            AmountPaid = request.AmountPaid,
            Notes = request.Notes,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        await db.Entry(customer).Reference(c => c.Animal).LoadAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = customer.Id }, customer.ToDto());
    }

    // PUT /api/ehlikurban/customers/8
    [HttpPut("{id:int}")]
    public async Task<ActionResult<CustomerDto>> Update(int id, CustomerRequest request, CancellationToken ct)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return NotFound();

        var capacityError = await CheckCapacityAsync(request.AnimalId, request.ShareCount, excludeCustomerId: id, ct);
        if (capacityError is not null) return capacityError;

        customer.FullName = request.FullName.Trim();
        customer.Phone = request.Phone.Trim();
        customer.AnimalId = request.AnimalId;
        customer.ShareCount = request.ShareCount;
        customer.AmountPaid = request.AmountPaid;
        customer.Notes = request.Notes;
        await db.SaveChangesAsync(ct);
        await db.Entry(customer).Reference(c => c.Animal).LoadAsync(ct);

        return Ok(customer.ToDto());
    }

    // DELETE /api/ehlikurban/customers/8
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return NotFound();

        db.Customers.Remove(customer);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<ActionResult?> CheckCapacityAsync(int? animalId, int shareCount, int? excludeCustomerId, CancellationToken ct)
    {
        if (animalId is null) return null;

        var animal = await db.Animals.AsNoTracking().FirstOrDefaultAsync(a => a.Id == animalId, ct);
        if (animal is null)
        {
            return Problem(title: "Hayvan bulunamadı.", statusCode: StatusCodes.Status404NotFound);
        }

        var taken = await db.Customers
            .Where(c => c.AnimalId == animalId && (excludeCustomerId == null || c.Id != excludeCustomerId))
            .SumAsync(c => c.ShareCount, ct);

        var free = animal.TotalShares - taken;
        if (shareCount > free)
        {
            return Problem(title: $"{animal.TagNumber} numaralı hayvanda yalnızca {free} boş hisse var.", statusCode: StatusCodes.Status409Conflict);
        }

        return null;
    }
}
