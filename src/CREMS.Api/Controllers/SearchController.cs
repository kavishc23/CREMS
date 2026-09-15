using CREMS.Api.Data;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController, Route("api/search"), Authorize(Policy = SystemPolicies.StaffPortal)]
public sealed class SearchController(ApplicationDbContext db, CurrentStaffScope staffScope, IAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Search([FromQuery] string? q, CancellationToken token)
    {
        var term = q?.Trim(); if (string.IsNullOrEmpty(term) || term.Length < 2) return Ok(Array.Empty<SearchHit>());
        if (term.Length > 100) return BadRequest(new { message = "Use a search term of 100 characters or fewer." });
        var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid();
        var result = new List<SearchHit>();
        var bookings = db.Bookings.AsNoTracking().Where(b => scope.IsAdministrator || scope.BranchIds.Contains(b.BranchId) && b.Items.Any() && b.Items.All(i => i.Asset != null && i.Asset.DivisionId.HasValue && scope.DivisionIds.Contains(i.Asset.DivisionId.Value)));
        if ((await authorization.AuthorizeAsync(User, SystemPolicies.ManageRentals)).Succeeded)
        {
            var customers = db.Customers.AsNoTracking().Where(c => scope.IsAdministrator || bookings.Any(b => b.CustomerId == c.Id));
            var cs = await customers.Where(c => c.Name.Contains(term) || c.CustomerNumber.Contains(term) || c.Email != null && c.Email.Contains(term) || c.Phone != null && c.Phone.Contains(term)).OrderBy(c => c.Name).Take(6).Select(c => new { c.Id, c.Name, c.CustomerNumber }).ToListAsync(token);
            result.AddRange(cs.Select(c => new SearchHit(c.Id, "Customer account", c.Name, c.CustomerNumber, "/staff/customers?search=" + Uri.EscapeDataString(c.CustomerNumber))));
            var bs = await bookings.Where(b => b.BookingNumber.Contains(term) || b.Customer!.Name.Contains(term)).OrderByDescending(b => b.CreatedAt).Take(6).Select(b => new { b.Id, b.BookingNumber, b.Status }).ToListAsync(token);
            result.AddRange(bs.Select(b => new SearchHit(b.Id, "Booking", b.BookingNumber, b.Status.ToString(), "/staff/bookings?search=" + Uri.EscapeDataString(b.BookingNumber))));
            var qs = await db.SalesQuotes.AsNoTracking().Where(x => (scope.IsAdministrator || scope.BranchIds.Contains(x.BranchId) && x.DivisionId.HasValue && scope.DivisionIds.Contains(x.DivisionId.Value)) && (x.QuoteNumber.Contains(term) || db.Customers.Any(c => c.Id == x.CustomerId && c.Name.Contains(term)))).OrderByDescending(x => x.CreatedAt).Take(6).Select(x => new { x.Id, x.QuoteNumber, x.Status }).ToListAsync(token);
            result.AddRange(qs.Select(x => new SearchHit(x.Id, "Quotation", x.QuoteNumber, x.Status.ToString(), "/staff/bookings?search=" + Uri.EscapeDataString(x.QuoteNumber))));
            var approvals = await db.ApprovalRequests.AsNoTracking().Where(a => a.EntityType == "Booking" && bookings.Any(b => b.Id == a.EntityId) && a.RequestNumber.Contains(term)).OrderByDescending(a => a.CreatedAt).Take(5).Select(a => new { a.Id, a.RequestNumber, a.Status, reference = db.Bookings.Where(b => b.Id == a.EntityId).Select(b => b.BookingNumber).FirstOrDefault() }).ToListAsync(token);
            result.AddRange(approvals.Select(a => new SearchHit(a.Id, "Approval", a.RequestNumber, a.Status.ToString(), "/staff/bookings?search=" + Uri.EscapeDataString(a.reference ?? a.RequestNumber))));
        }
        if ((await authorization.AuthorizeAsync(User, SystemPolicies.ManageFinance)).Succeeded)
        {
            var invoices = await db.RentalInvoices.AsNoTracking().Where(i => bookings.Any(b => b.Id == i.BookingId) && (i.InvoiceNumber.Contains(term) || i.Booking!.BookingNumber.Contains(term))).OrderByDescending(i => i.CreatedAt).Take(5).Select(i => new { i.Id, i.InvoiceNumber, i.Status, i.Booking!.BookingNumber }).ToListAsync(token);
            result.AddRange(invoices.Select(i => new SearchHit(i.Id, "Invoice", i.InvoiceNumber, i.Status.ToString(), "/staff/bookings?search=" + Uri.EscapeDataString(i.BookingNumber))));
        }
        var assets = db.Assets.AsNoTracking().Where(a => scope.IsAdministrator || scope.BranchIds.Contains(a.BranchId) && a.DivisionId.HasValue && scope.DivisionIds.Contains(a.DivisionId.Value));
        if ((await authorization.AuthorizeAsync(User, SystemPolicies.ViewAssets)).Succeeded)
        {
            var matches = await assets.Where(a => a.Name.Contains(term) || a.AssetNumber.Contains(term) || a.RegistrationNumber != null && a.RegistrationNumber.Contains(term) || a.SerialNumber != null && a.SerialNumber.Contains(term)).OrderBy(a => a.AssetNumber).Take(6).Select(a => new { a.Id, a.Name, a.AssetNumber }).ToListAsync(token);
            result.AddRange(matches.Select(a => new SearchHit(a.Id, "Asset", a.Name, a.AssetNumber, "/staff/assets?search=" + Uri.EscapeDataString(a.AssetNumber))));
        }
        if ((await authorization.AuthorizeAsync(User, SystemPolicies.ManageMaintenance)).Succeeded)
        {
            var jobs = await db.MaintenanceJobs.AsNoTracking().Where(j => assets.Any(a => a.Id == j.AssetId) && (j.JobNumber.Contains(term) || j.FaultDescription.Contains(term))).OrderByDescending(j => j.CreatedAt).Take(5).Select(j => new { j.Id, j.JobNumber, j.Status }).ToListAsync(token);
            result.AddRange(jobs.Select(j => new SearchHit(j.Id, "Maintenance job", j.JobNumber, j.Status.ToString(), "/staff/maintenance?search=" + Uri.EscapeDataString(j.JobNumber))));
        }
        return Ok(result.OrderByDescending(x => x.Title.Equals(term, StringComparison.OrdinalIgnoreCase) || x.Detail.Equals(term, StringComparison.OrdinalIgnoreCase)).ThenBy(x => x.Kind).ToList());
    }
}
public sealed record SearchHit(Guid Id, string Kind, string Title, string Detail, string Url);
