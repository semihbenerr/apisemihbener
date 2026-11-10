using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SemihBener.Abstractions;
using SemihBener.Plugin.EhliKurban.Contracts;
using SemihBener.Plugin.EhliKurban.Data;

namespace SemihBener.Plugin.EhliKurban.Controllers;

// /api/ehlikurban/animals
[Route("animals")]
public sealed class AnimalsController(EhliKurbanDbContext db) : PluginControllerBase
{
    // GET /api/ehlikurban/animals?type=Cattle&onlyAvailable=true
    [HttpGet]
    public async Task<ActionResult<List<AnimalDto>>> List(
        [FromQuery] AnimalType? type,
        [FromQuery] AnimalStatus? status,
        [FromQuery] bool onlyAvailable = false,
        CancellationToken ct = default)
    {
        var query = db.Animals.AsNoTracking();
        if (type is not null) query = query.Where(a => a.Type == type);
        if (status is not null) query = query.Where(a => a.Status == status);

        var rows = await query
            .OrderBy(a => a.TagNumber)
            .Select(a => new { Animal = a, Taken = a.Customers.Sum(c => c.ShareCount) })
            .ToListAsync(ct);

        var result = rows.Select(r => r.Animal.ToDto(r.Taken));
        if (onlyAvailable) result = result.Where(a => a.FreeShares > 0);

        return Ok(result.ToList());
    }

    // GET /api/ehlikurban/animals/3
    [HttpGet("{id:int}")]
    public async Task<ActionResult<AnimalDetailDto>> GetById(int id, CancellationToken ct)
    {
        var animal = await db.Animals.AsNoTracking()
            .Include(a => a.Customers)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (animal is null) return NotFound();

        var customers = animal.Customers
            .OrderBy(c => c.FullName)
            .Select(c => { c.Animal = animal; return c.ToDto(); })
            .ToList();

        return Ok(new AnimalDetailDto(animal.ToDto(animal.Customers.Sum(c => c.ShareCount)), customers));
    }

    // POST /api/ehlikurban/animals
    [HttpPost]
    public async Task<ActionResult<AnimalDto>> Create(AnimalRequest request, CancellationToken ct)
    {
        var totalShares = ResolveShares(request);
        if (totalShares is null) return ValidationProblem(ModelState);

        var tag = request.TagNumber.Trim();
        if (await db.Animals.AnyAsync(a => a.TagNumber == tag, ct))
        {
            return Problem(title: $"'{tag}' küpe numarası zaten kayıtlı.", statusCode: StatusCodes.Status409Conflict);
        }

        var animal = new Animal
        {
            TagNumber = tag,
            Type = request.Type,
            WeightKg = request.WeightKg,
            Price = request.Price,
            TotalShares = totalShares.Value,
            Status = request.Status ?? AnimalStatus.Waiting,
            Notes = request.Notes,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Animals.Add(animal);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = animal.Id }, animal.ToDto(0));
    }

    // PUT /api/ehlikurban/animals/3
    [HttpPut("{id:int}")]
    public async Task<ActionResult<AnimalDto>> Update(int id, AnimalRequest request, CancellationToken ct)
    {
        var animal = await db.Animals.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (animal is null) return NotFound();

        var totalShares = ResolveShares(request);
        if (totalShares is null) return ValidationProblem(ModelState);

        var tag = request.TagNumber.Trim();
        if (await db.Animals.AnyAsync(a => a.TagNumber == tag && a.Id != id, ct))
        {
            return Problem(title: $"'{tag}' küpe numarası başka bir hayvanda kayıtlı.", statusCode: StatusCodes.Status409Conflict);
        }

        var taken = await db.Customers.Where(c => c.AnimalId == id).SumAsync(c => c.ShareCount, ct);
        if (totalShares.Value < taken)
        {
            return Problem(title: $"Bu hayvanda {taken} hisse dolu; toplam hisse {taken}'den az olamaz.", statusCode: StatusCodes.Status409Conflict);
        }

        animal.TagNumber = tag;
        animal.Type = request.Type;
        animal.WeightKg = request.WeightKg;
        animal.Price = request.Price;
        animal.TotalShares = totalShares.Value;
        animal.Status = request.Status ?? animal.Status;
        animal.Notes = request.Notes;
        await db.SaveChangesAsync(ct);

        return Ok(animal.ToDto(taken));
    }

    // DELETE /api/ehlikurban/animals/3
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var animal = await db.Animals.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (animal is null) return NotFound();

        if (await db.Customers.AnyAsync(c => c.AnimalId == id, ct))
        {
            return Problem(title: "Hissedarı olan hayvan silinemez. Önce hissedarları başka hayvana taşıyın.", statusCode: StatusCodes.Status409Conflict);
        }

        db.Animals.Remove(animal);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private int? ResolveShares(AnimalRequest request)
    {
        var shares = request.TotalShares ?? (request.Type == AnimalType.Cattle ? 7 : 1);

        if (request.Type != AnimalType.Cattle && shares != 1)
        {
            ModelState.AddModelError(nameof(request.TotalShares), "Küçükbaş hayvanlar tek hisselidir.");
            return null;
        }

        return shares;
    }
}
