using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using SkiaSharp;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class MaintenanceDocumentsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "crems-maintenance-documents-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("png", "FaultPhoto")]
    [InlineData("jpg", "CompletionEvidence")]
    [InlineData("pdf", "Invoice")]
    public async Task Readable_documents_round_trip(string extension, string category)
    {
        await using var db = MaintenanceJobsTests.Database();
        var (controller, job) = await Setup(db);
        var bytes = Document(extension);
        Assert.IsType<OkObjectResult>(await controller.Upload(job.Id, File("audit." + extension, bytes), category, TestContext.Current.CancellationToken));
        var record = await db.DocumentRecords.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(category, record.Type);
        Assert.IsType<OkObjectResult>(await controller.List(job.Id, TestContext.Current.CancellationToken));
        var result = Assert.IsType<PhysicalFileResult>(await controller.Download(job.Id, record.Id, TestContext.Current.CancellationToken));
        Assert.Equal(bytes, await System.IO.File.ReadAllBytesAsync(result.FileName, TestContext.Current.CancellationToken));
        Assert.Equal("audit." + extension, result.FileDownloadName);
        Assert.IsType<NotFoundResult>(await controller.Download(job.Id, Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("pdf")]
    public async Task Headers_without_document_content_are_rejected(string extension)
    {
        await using var db = MaintenanceJobsTests.Database();
        var (controller, job) = await Setup(db);
        byte[] bytes = extension switch { "png" => [137, 80, 78, 71, 13, 10, 26, 10], "jpg" => [255, 216, 255], _ => "%PDF-1.7\n%%EOF"u8.ToArray() };
        Assert.IsType<BadRequestObjectResult>(await controller.Upload(job.Id, File("broken." + extension, bytes), "FaultPhoto", TestContext.Current.CancellationToken));
        Assert.Empty(db.DocumentRecords);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Truncated_image_wrong_extension_empty_oversize_and_invalid_category_are_rejected()
    {
        await using var db = MaintenanceJobsTests.Database();
        var (controller, job) = await Setup(db);
        var png = Document("png");
        foreach (var file in new[] { File("truncated.png", png[..(png.Length / 2)]), File("wrong.jpg", png), File("empty.png", []),
            File("large.png", new byte[5_242_881]), File("image.exe", png) })
            Assert.IsType<BadRequestObjectResult>(await controller.Upload(job.Id, file, "Invoice", TestContext.Current.CancellationToken));
        Assert.IsType<BadRequestObjectResult>(await controller.Upload(job.Id, File("image.png", png), "Other", TestContext.Current.CancellationToken));
        Assert.Empty(db.DocumentRecords);
    }

    [Fact]
    public async Task Documents_require_access_to_the_jobs_branch_and_division()
    {
        await using var db = MaintenanceJobsTests.Database();
        var (controller, job) = await Setup(db);
        await controller.Upload(job.Id, File("image.png", Document("png")), "FaultPhoto", TestContext.Current.CancellationToken);
        var record = await db.DocumentRecords.SingleAsync(TestContext.Current.CancellationToken);
        var (staff, _) = await MaintenanceJobsTests.Setup(db, administrator: false);
        controller = new MaintenanceDocumentsController(db, new CurrentStaffScope(db), new TestEnvironment(root)) { ControllerContext = staff.ControllerContext };
        Assert.IsType<ForbidResult>(await controller.List(job.Id, TestContext.Current.CancellationToken));
        Assert.IsType<ForbidResult>(await controller.Upload(job.Id, File("image.png", Document("png")), "FaultPhoto", TestContext.Current.CancellationToken));
        Assert.IsType<ForbidResult>(await controller.Download(job.Id, record.Id, TestContext.Current.CancellationToken));
        Assert.Single(await db.DocumentRecords.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Invoice_evidence_requires_financial_permission_for_reads_and_writes()
    {
        await using var db = MaintenanceJobsTests.Database(); var (controller, job) = await Setup(db);
        await controller.Upload(job.Id, File("invoice.pdf", Document("pdf")), "Invoice", TestContext.Current.CancellationToken);
        var invoice = await db.DocumentRecords.SingleAsync(TestContext.Current.CancellationToken);
        var user = await db.Users.SingleAsync(TestContext.Current.CancellationToken);
        db.UserPermissionOverrides.Add(new UserPermissionOverride { UserId = user.Id, Permission = SystemPermissions.AssetsViewFinancials, IsGranted = false });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var result = Assert.IsType<OkObjectResult>(await controller.List(job.Id, TestContext.Current.CancellationToken));
        Assert.Equal("[]", System.Text.Json.JsonSerializer.Serialize(result.Value));
        Assert.IsType<ForbidResult>(await controller.Download(job.Id, invoice.Id, TestContext.Current.CancellationToken));
        Assert.IsType<ForbidResult>(await controller.Upload(job.Id, File("invoice.pdf", Document("pdf")), "Invoice", TestContext.Current.CancellationToken));
        Assert.IsType<OkObjectResult>(await controller.Upload(job.Id, File("repair.png", Document("png")), "CompletionEvidence", TestContext.Current.CancellationToken));
    }

    private async Task<(MaintenanceDocumentsController, MaintenanceJob)> Setup(ApplicationDbContext db)
    {
        var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        await jobs.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), TestContext.Current.CancellationToken);
        return (new MaintenanceDocumentsController(db, new CurrentStaffScope(db), new TestEnvironment(root)) { ControllerContext = jobs.ControllerContext },
            await db.MaintenanceJobs.SingleAsync(TestContext.Current.CancellationToken));
    }

    private static IFormFile File(string name, byte[] bytes) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name);

    private static byte[] Document(string extension)
    {
        using var stream = new MemoryStream();
        if (extension == "pdf")
        {
            using var pdf = SKDocument.CreatePdf(stream);
            var canvas = pdf.BeginPage(100, 100);
            canvas.Clear(SKColors.White);
            pdf.EndPage(); pdf.Close();
            return stream.ToArray();
        }
        using var bitmap = new SKBitmap(24, 24);
        bitmap.Erase(SKColors.Blue);
        bitmap.Encode(stream, extension == "png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, 100);
        return stream.ToArray();
    }

    public void Dispose()
    {
        // This unique test-owned directory is always a direct child of the system temp directory.
        if (Path.GetDirectoryName(Path.GetFullPath(root)) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
            && Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public string WebRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
