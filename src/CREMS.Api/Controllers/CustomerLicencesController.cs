using CREMS.Api.Data;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
public sealed class CustomerLicencesController(CustomerLicenceService licences, UserManager<ApplicationUser> users,
    ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet("api/customer-account/licence")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> Mine(CancellationToken token)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is not Guid customerId) return Unauthorized();
        return Ok(ToResponse(await licences.CurrentAsync(customerId, token)));
    }

    [HttpPost("api/customer-account/licence/scan")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    [RequestSizeLimit(CustomerLicenceService.MaximumFileSize + 64_000)]
    public async Task<ActionResult> ScanMine(IFormFile file, CancellationToken token)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is not Guid customerId) return Unauthorized();
        try { return Ok(await licences.ScanAsync(customerId, file, LicenceUploadSource.Customer, user.Id, token)); }
        catch (LicenceException e) { return BadRequest(new { code = e.Code, message = e.Message }); }
    }

    [HttpPost("api/customer-account/licence/{submissionId:guid}/confirm")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> ConfirmMine(Guid submissionId, ConfirmLicenceRequest request, CancellationToken token)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is not Guid customerId) return Unauthorized();
        try { return Ok(ToResponse(await licences.ConfirmAsync(customerId, submissionId, request.LicenceNumber, request.Classes, user.Id, true, token))); }
        catch (LicenceException e) { return BadRequest(new { code = e.Code, message = e.Message }); }
    }

    [HttpGet("api/customer-account/licence/image")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> MineImage(CancellationToken token)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is not Guid customerId) return Unauthorized();
        return await LicenceFile(customerId, token);
    }

    [HttpGet("api/customers/{customerId:guid}/licence")]
    [Authorize(Policy = SystemPermissions.CustomersManageAccess)]
    public async Task<ActionResult> Customer(Guid customerId, CancellationToken token) => Ok(ToResponse(await licences.CurrentAsync(customerId, token)));

    [HttpPost("api/customers/{customerId:guid}/licence/scan")]
    [Authorize(Policy = SystemPermissions.CustomersManageAccess)]
    [RequestSizeLimit(CustomerLicenceService.MaximumFileSize + 64_000)]
    public async Task<ActionResult> ScanCustomer(Guid customerId, IFormFile file, CancellationToken token)
    {
        var actor = await users.GetUserAsync(User);
        try { return Ok(await licences.ScanAsync(customerId, file, LicenceUploadSource.Administrator, actor?.Id, token)); }
        catch (LicenceException e) { return BadRequest(new { code = e.Code, message = e.Message }); }
    }

    [HttpPost("api/customers/{customerId:guid}/licence/{submissionId:guid}/confirm")]
    [Authorize(Policy = SystemPermissions.CustomersManageAccess)]
    public async Task<ActionResult> ConfirmCustomer(Guid customerId, Guid submissionId, ConfirmLicenceRequest request, CancellationToken token)
    {
        var actor = await users.GetUserAsync(User);
        try { return Ok(ToResponse(await licences.ConfirmAsync(customerId, submissionId, request.LicenceNumber, request.Classes, actor?.Id, true, token))); }
        catch (LicenceException e) { return BadRequest(new { code = e.Code, message = e.Message }); }
    }

    [HttpPost("api/customers/{customerId:guid}/licence/manual")]
    [Authorize(Policy = SystemPermissions.CustomersManageAccess)]
    [RequestSizeLimit(CustomerLicenceService.MaximumFileSize + 64_000)]
    public async Task<ActionResult> Manual(Guid customerId, IFormFile file, [FromForm] string licenceNumber, [FromForm] string classes, CancellationToken token)
    {
        var actor = await users.GetUserAsync(User);
        try
        {
            var values = classes.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
            return Ok(ToResponse(await licences.SaveManualAsync(customerId, file, licenceNumber, values, LicenceUploadSource.Administrator, actor?.Id, token)));
        }
        catch (FormatException) { return BadRequest(new { code = "invalid_licence_class", message = "Licence classes must be numbers from 1 through 9." }); }
        catch (LicenceException e) { return BadRequest(new { code = e.Code, message = e.Message }); }
    }

    [HttpGet("api/customers/{customerId:guid}/licence/image")]
    [Authorize(Policy = SystemPermissions.CustomersManageAccess)]
    public Task<ActionResult> CustomerImage(Guid customerId, CancellationToken token) => LicenceFile(customerId, token);

    [HttpDelete("api/customers/{customerId:guid}/licence")]
    [Authorize(Policy = SystemPermissions.CustomersManageAccess)]
    public async Task<ActionResult> RemoveCustomerLicence(Guid customerId, CancellationToken token)
    {
        if (!User.IsInRole(SystemRoles.SuperAdministrator) && !User.IsInRole(SystemRoles.Administrator)) return Forbid();
        var scope = await staffScope.GetAsync(User);
        if (scope is null) return Forbid();
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == customerId, token);
        if (customer is null) return NotFound();
        await licences.RemoveAsync(customerId, token);
        AuditWriter.Record(db, scope, "Customer driver licence removed", nameof(Customer), customerId,
            $"The stored driver licence for {customer.CustomerNumber} was removed by an administrator.", scope.BranchId);
        await db.SaveChangesAsync(token);
        return NoContent();
    }

    private async Task<ActionResult> LicenceFile(Guid customerId, CancellationToken token)
    {
        var licence = await licences.CurrentAsync(customerId, token);
        if (licence is null) return NotFound();
        try { return PhysicalFile(licences.ResolvePath(licence), licence.ContentType, enableRangeProcessing: true); }
        catch (FileNotFoundException) { return NotFound(new { message = "The stored licence image is unavailable." }); }
    }

    private static object ToResponse(CustomerLicence? item) => item is null
        ? new { status = "Required", licenceNumber = (string?)null, classes = Array.Empty<int>(), hasImage = false }
        : new { status = "Verified", licenceNumber = item.LicenceNumber, classes = item.LicenceClasses.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse), hasImage = true, updatedAt = item.UpdatedAt };
}

public sealed record ConfirmLicenceRequest(string LicenceNumber, IReadOnlyList<int> Classes);
