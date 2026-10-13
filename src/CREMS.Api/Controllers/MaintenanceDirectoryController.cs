using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/administration/maintenance-directory")]
[Authorize(Policy = SystemPolicies.AdministerSystem)]
public sealed class MaintenanceDirectoryController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get(CancellationToken token)
    {
        return Ok(new {
            technicians = await db.Personnel.AsNoTracking().Where(x => x.Type == PersonnelType.Technician).OrderBy(x => x.FullName)
                .Select(x => new { x.Id, x.EmployeeNumber, x.FullName, x.BranchId, x.DivisionId, x.StandardCostRate, x.Availability, x.IsActive, locations = x.Locations.Select(location => new { location.BranchId, location.DivisionId }).ToList(), version = x.UpdatedAt ?? x.CreatedAt }).ToListAsync(token),
            suppliers = await db.Suppliers.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.SupplierNumber, x.Name, x.Email, x.Phone, x.Address, x.TaxNumber, x.PaymentTermsDays, x.IsActive, version = x.UpdatedAt ?? x.CreatedAt }).ToListAsync(token),
            locations = await db.BranchDivisions.AsNoTracking().Where(x => x.IsActive && x.Branch!.IsActive && x.Division!.IsActive)
                .OrderBy(x => x.Branch!.Name).ThenBy(x => x.Division!.Name)
                .Select(x => new { x.BranchId, branchName = x.Branch!.Name, x.DivisionId, divisionName = x.Division!.Name }).ToListAsync(token)
        });
    }

    [HttpPost("technicians")]
    public Task<ActionResult> CreateTechnician(TechnicianDirectoryRequest request, CancellationToken token) => SaveTechnician(null, request, token);

    [HttpPut("technicians/{id:guid}")]
    public Task<ActionResult> UpdateTechnician(Guid id, TechnicianDirectoryRequest request, CancellationToken token) => SaveTechnician(id, request, token);

    private async Task<ActionResult> SaveTechnician(Guid? id, TechnicianDirectoryRequest request, CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid();
        var item = id.HasValue ? await db.Personnel.Include(x => x.Locations).SingleOrDefaultAsync(x => x.Id == id && x.Type == PersonnelType.Technician, token) : null;
        if (id.HasValue && item is null) return NotFound();
        if (item is not null && request.ExpectedVersion != (item.UpdatedAt ?? item.CreatedAt)) return Conflict(new { message = "This technician changed. Refresh the directory before editing again." });
        if (string.IsNullOrWhiteSpace(request.FullName)) return BadRequest(new { message = "Enter the technician name." });
        if (request.StandardCostRate < 0 || request.StandardCostRate > 1000000 || request.StandardCostRate != decimal.Round(request.StandardCostRate, 2)) return BadRequest(new { message = "Enter an hourly labour rate from 0 to 1,000,000 with at most two decimal places." });
        if (!Enum.IsDefined(request.Availability)) return BadRequest(new { message = "Select a valid availability." });
        // Older clients can continue sending the original single location.
        var locations = (request.Locations ?? [new(request.BranchId, request.DivisionId)]).Distinct().ToArray();
        if (locations.Length == 0 || locations.Length > 100) return BadRequest(new { message = "Select between 1 and 100 branch/division assignments." });
        foreach (var location in locations) {
            var existing = item is not null && (item.Locations.Any(x => x.BranchId == location.BranchId && x.DivisionId == location.DivisionId) ||
                item.Locations.Count == 0 && item.BranchId == location.BranchId && item.DivisionId == location.DivisionId);
            if ((request.IsActive || !existing) && !await db.BranchDivisions.AnyAsync(x => x.BranchId == location.BranchId && x.DivisionId == location.DivisionId && x.IsActive && x.Branch!.IsActive && x.Division!.IsActive, token))
                return BadRequest(new { message = "Select active branch/division combinations for every assignment." });
        }
        var number = item?.EmployeeNumber;
        if (number is null) {
            do { number = $"EMP-{Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()}"; }
            while (await db.Personnel.AnyAsync(x => x.EmployeeNumber == number, token));
        }
        var before = item is null ? null : $"{item.FullName}; rate {item.StandardCostRate}; active {item.IsActive}; locations {string.Join(", ", item.Locations.Select(x => $"{x.BranchId}/{x.DivisionId}"))}; legacy location {item.BranchId}/{item.DivisionId}";
        if (item is null) { item = new Personnel { EmployeeNumber = number, FullName = request.FullName.Trim(), Type = PersonnelType.Technician }; db.Personnel.Add(item); }
        item.EmployeeNumber = number; item.FullName = request.FullName.Trim(); item.BranchId = locations[0].BranchId; item.DivisionId = locations[0].DivisionId;
        foreach (var old in item.Locations.Where(x => !locations.Contains(new TechnicianLocationRequest(x.BranchId, x.DivisionId))).ToArray()) {
            db.PersonnelLocations.Remove(old); item.Locations.Remove(old);
        }
        foreach (var location in locations.Where(location => !item.Locations.Any(x => x.BranchId == location.BranchId && x.DivisionId == location.DivisionId)))
            item.Locations.Add(new PersonnelLocation { PersonnelId = item.Id, BranchId = location.BranchId, DivisionId = location.DivisionId });
        item.StandardCostRate = request.StandardCostRate; item.Availability = request.Availability; item.IsActive = request.IsActive; item.UpdatedAt = DateTimeOffset.UtcNow;
        AuditWriter.Record(db, scope, "Maintenance technician saved", nameof(Personnel), item.Id, $"{item.EmployeeNumber}: technician directory updated.", item.BranchId,
            before, $"{item.FullName}; rate {item.StandardCostRate}; active {item.IsActive}; locations {string.Join(", ", locations.Select(x => $"{x.BranchId}/{x.DivisionId}"))}");
        await db.SaveChangesAsync(token);
        return Ok(new { item.Id });
    }

    [HttpPost("suppliers")]
    public Task<ActionResult> CreateSupplier(SupplierDirectoryRequest request, CancellationToken token) => SaveSupplier(null, request, token);

    [HttpPut("suppliers/{id:guid}")]
    public Task<ActionResult> UpdateSupplier(Guid id, SupplierDirectoryRequest request, CancellationToken token) => SaveSupplier(id, request, token);

    private async Task<ActionResult> SaveSupplier(Guid? id, SupplierDirectoryRequest request, CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid();
        var item = id.HasValue ? await db.Suppliers.SingleOrDefaultAsync(x => x.Id == id, token) : null;
        if (id.HasValue && item is null) return NotFound();
        if (item is not null && request.ExpectedVersion != (item.UpdatedAt ?? item.CreatedAt)) return Conflict(new { message = "This supplier changed. Refresh the directory before editing again." });
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.SupplierNumber)) return BadRequest(new { message = "Enter the supplier name and number." });
        var number = request.SupplierNumber.Trim().ToUpperInvariant();
        if (await db.Suppliers.AnyAsync(x => x.Id != id && x.SupplierNumber.ToUpper() == number, token)) return Conflict(new { message = "That supplier number already exists." });
        var before = item is null ? null : $"{item.Name}; active {item.IsActive}";
        if (item is null) { item = new Supplier { SupplierNumber = number, Name = request.Name.Trim() }; db.Suppliers.Add(item); }
        item.SupplierNumber = number; item.Name = request.Name.Trim(); item.Email = Clean(request.Email); item.Phone = Clean(request.Phone);
        item.Address = Clean(request.Address); item.TaxNumber = Clean(request.TaxNumber); item.PaymentTermsDays = request.PaymentTermsDays;
        item.IsActive = request.IsActive; item.UpdatedAt = DateTimeOffset.UtcNow;
        AuditWriter.Record(db, scope, "Maintenance supplier saved", nameof(Supplier), item.Id, $"{item.SupplierNumber}: supplier directory updated.", null, before, $"{item.Name}; active {item.IsActive}");
        await db.SaveChangesAsync(token);
        return Ok(new { item.Id });
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record TechnicianDirectoryRequest([Required, StringLength(200)] string FullName,
    Guid BranchId, Guid DivisionId, [Range(0, 1000000)] decimal StandardCostRate,
    [EnumDataType(typeof(PersonnelAvailability))] PersonnelAvailability Availability = PersonnelAvailability.Available,
    bool IsActive = true, DateTimeOffset? ExpectedVersion = null, TechnicianLocationRequest[]? Locations = null);
public sealed record TechnicianLocationRequest(Guid BranchId, Guid DivisionId);
public sealed record SupplierDirectoryRequest([Required, StringLength(100)] string SupplierNumber, [Required, StringLength(200)] string Name,
    [EmailAddress, StringLength(200)] string? Email = null, [StringLength(80)] string? Phone = null, [StringLength(1000)] string? Address = null,
    [StringLength(100)] string? TaxNumber = null, [Range(0, 365)] int PaymentTermsDays = 30, bool IsActive = true, DateTimeOffset? ExpectedVersion = null);
