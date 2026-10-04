using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/maintenance-jobs/{jobId:guid}/documents")]
[Authorize(Policy = SystemPolicies.ManageMaintenance)]
public sealed class MaintenanceDocumentsController(ApplicationDbContext db, CurrentStaffScope staffScope, IWebHostEnvironment environment) : ControllerBase
{
    private async Task<bool> CanAccess(Guid id, CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User);
        var job = await db.MaintenanceJobs.Include(x => x.Asset).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        return scope is not null && job is not null && scope.HasAssetAccess(job.BranchId, job.Asset?.DivisionId);
    }

    [HttpGet]
    public async Task<ActionResult> List(Guid jobId, CancellationToken token)
    {
        if (!await CanAccess(jobId, token)) return Forbid();
        return Ok(await db.DocumentRecords.AsNoTracking().Where(x => x.EntityType == nameof(MaintenanceJob) && x.EntityId == jobId)
            .Select(x => new { x.Id, x.FileName, x.Type }).ToListAsync(token));
    }

    [HttpPost]
    [RequestSizeLimit(5_500_000)]
    public async Task<ActionResult> Upload(Guid jobId, [FromForm] IFormFile file, [FromForm] string type, CancellationToken token)
    {
        if (!await CanAccess(jobId, token)) return Forbid();
        if (type is not ("FaultPhoto" or "Invoice" or "CompletionEvidence")) return BadRequest(new { message = "Select a document category." });
        if (file.Length is <= 0 or > 5_242_880) return BadRequest(new { message = "Choose a PDF, JPEG or PNG up to 5 MB." });
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, token);
        var bytes = stream.ToArray();
        var valid = extension switch {
            ".pdf" => bytes.Length >= 5 && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
            ".jpg" or ".jpeg" => bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
            ".png" => bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            _ => false
        };
        if (!valid) return BadRequest(new { message = "The file must be a valid PDF, JPEG or PNG." });
        var root = Path.Combine(environment.ContentRootPath, "App_Data", "maintenance-documents");
        Directory.CreateDirectory(root);
        var storageName = $"{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(root, storageName);
        await System.IO.File.WriteAllBytesAsync(path, bytes, token);
        var job = await db.MaintenanceJobs.SingleAsync(x => x.Id == jobId, token);
        var record = new DocumentRecord { DocumentNumber = $"MNT-DOC-{Guid.NewGuid():N}", EntityType = nameof(MaintenanceJob),
            EntityId = jobId, BranchId = job.BranchId, Type = type, FileName = Path.GetFileName(file.FileName), StoragePath = storageName };
        db.DocumentRecords.Add(record);
        var scope = await staffScope.GetAsync(User);
        AuditWriter.Record(db, scope!, "Maintenance document attached", nameof(MaintenanceJob), jobId,
            $"{type}: {record.FileName}", job.BranchId);
        try { await db.SaveChangesAsync(token); }
        catch { System.IO.File.Delete(path); throw; }
        return Ok(new { record.Id, record.FileName, record.Type });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult> Download(Guid jobId, Guid id, CancellationToken token)
    {
        if (!await CanAccess(jobId, token)) return Forbid();
        var record = await db.DocumentRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.EntityType == nameof(MaintenanceJob) && x.EntityId == jobId, token);
        if (record is null) return NotFound();
        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "maintenance-documents"));
        var path = Path.GetFullPath(Path.Combine(root, record.StoragePath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !System.IO.File.Exists(path)) return NotFound();
        var contentType = Path.GetExtension(path) switch { ".pdf" => "application/pdf", ".png" => "image/png", _ => "image/jpeg" };
        return PhysicalFile(path, contentType, record.FileName);
    }
}
