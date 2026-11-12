using System.Security.Cryptography;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/bookings/{bookingId:guid}/documents")]
[Authorize(Policy = SystemPolicies.ManageRentals)]
public sealed class BookingDocumentsController(ApplicationDbContext db, CurrentStaffScope staffScope, IWebHostEnvironment environment) : ControllerBase
{
    private async Task<Booking?> Accessible(Guid id, CancellationToken token)
    {
        var booking = await db.Bookings.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Asset).ThenInclude(x => x!.ServiceOffering).SingleOrDefaultAsync(x => x.Id == id, token);
        var scope = await staffScope.GetAsync(User);
        return booking is not null && scope is not null && scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId) ? booking : null;
    }

    [HttpPost]
    [RequestSizeLimit(5_500_000)]
    public async Task<ActionResult> Upload(Guid bookingId, IFormFile file, [FromForm] string type, CancellationToken token)
    {
        var booking = await Accessible(bookingId, token);
        if (booking is null) return NotFound();
        var name = BookingDocuments.Required(booking.Items.FirstOrDefault()?.Asset).FirstOrDefault(x => string.Equals(x, type.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is null) return BadRequest(new { message = "Select a configured document requirement for this booking." });
        if (file.Length is <= 0 or > 5_242_880) return BadRequest(new { message = "Choose a PDF, JPEG or PNG up to 5 MB." });
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        await using var content = new MemoryStream(); await file.CopyToAsync(content, token);
        var bytes = content.ToArray();
        if (!BookingDocuments.ValidFile(extension, bytes)) return BadRequest(new { message = "The file must be a valid PDF, JPEG or PNG." });
        var storage = Path.Combine(booking.CustomerId.ToString("N"), $"{Guid.NewGuid():N}{extension}");
        var path = Path.Combine(environment.ContentRootPath, "App_Data", "customer-documents", storage);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); await System.IO.File.WriteAllBytesAsync(path, bytes, token);
        var record = new DocumentRecord { DocumentNumber = $"DOC-{Guid.NewGuid():N}", EntityType = nameof(Booking), EntityId = bookingId,
            BranchId = booking.BranchId, Type = name, FileName = Path.GetFileName(file.FileName), StoragePath = storage, ContentHash = Convert.ToHexString(SHA256.HashData(bytes)) };
        db.DocumentRecords.Add(record);
        AuditWriter.Record(db, (await staffScope.GetAsync(User))!, "Booking document attached", nameof(Booking), bookingId, $"{name}: {record.FileName}", booking.BranchId);
        await db.SaveChangesAsync(token);
        return Ok(new { record.Id, record.FileName, record.Type, record.CreatedAt });
    }

    [HttpGet("{documentId:guid}/download")]
    public async Task<ActionResult> Download(Guid bookingId, Guid documentId, CancellationToken token)
    {
        if (await Accessible(bookingId, token) is null) return NotFound();
        var record = await db.DocumentRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId && x.EntityType == nameof(Booking) && x.EntityId == bookingId, token);
        if (record is null) return NotFound();
        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "customer-documents"));
        var path = Path.GetFullPath(Path.Combine(root, record.StoragePath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, Path.GetExtension(path).ToLowerInvariant() switch { ".pdf" => "application/pdf", ".png" => "image/png", _ => "image/jpeg" }, record.FileName);
    }
}
