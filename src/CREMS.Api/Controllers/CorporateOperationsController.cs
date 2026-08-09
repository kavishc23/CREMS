using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/corporate-operations")]
[Authorize(Policy = SystemPolicies.StaffPortal)]
public sealed class CorporateOperationsController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult> Overview(CancellationToken token)
    {
        var scope = await Scope(); if (scope is null) return Forbid(); var now = DateTimeOffset.UtcNow;
        var bookings = Scoped(db.Bookings.AsNoTracking(), scope, x => x.BranchId);
        var assets = Scoped(db.Assets.AsNoTracking(), scope, x => x.BranchId);
        var rentalDays = await bookings.Where(x => x.Status == BookingStatus.ConvertedToRental).SelectMany(x => x.Items).SumAsync(x => (decimal?)EF.Functions.DateDiffDay(x.StartAt, x.EndAt), token) ?? 0;
        var revenue = await bookings.Where(x => x.Status == BookingStatus.Completed || x.Status == BookingStatus.ConvertedToRental).SelectMany(x => x.Items).SumAsync(x => (decimal?)(x.DailyRate * Math.Max(1, EF.Functions.DateDiffDay(x.StartAt, x.EndAt))), token) ?? 0;
        var assetCount = await assets.CountAsync(x => x.IsActive, token); var rented = await assets.CountAsync(x => x.Status == AssetStatus.Rented, token);
        var invoices = db.RentalInvoices.AsNoTracking().Where(x => db.Bookings.Any(b => b.Id == x.BookingId && (scope.IsAdministrator || b.BranchId == scope.BranchId)));
        return Ok(new { activeRentals = await bookings.CountAsync(x => x.Status == BookingStatus.ConvertedToRental, token), overdueRentals = await bookings.CountAsync(x => x.Status == BookingStatus.ConvertedToRental && x.Items.Any(i => i.EndAt < now), token), pendingQuotes = await Scoped(db.SalesQuotes.AsNoTracking(), scope, x => x.BranchId).CountAsync(x => x.Status == QuoteStatus.Draft || x.Status == QuoteStatus.Sent || x.Status == QuoteStatus.Negotiating, token), todayDispatches = await Scoped(db.DispatchJobs.AsNoTracking(), scope, x => x.BranchId).CountAsync(x => x.ScheduledAt.Date == now.Date && x.Status != DispatchStatus.Completed && x.Status != DispatchStatus.Cancelled, token), pendingApprovals = await Scoped(db.ApprovalRequests.AsNoTracking(), scope, x => x.BranchId).CountAsync(x => x.Status == ApprovalStatus.Pending, token), lowStockParts = await Scoped(db.InventoryParts.AsNoTracking(), scope, x => x.BranchId).CountAsync(x => x.QuantityOnHand - x.QuantityAllocated <= x.ReorderLevel, token), openCases = await Scoped(db.CustomerCases.AsNoTracking(), scope, x => x.BranchId).CountAsync(x => x.Status != CaseStatus.Resolved && x.Status != CaseStatus.Closed, token), activeTasks = await ScopedNullable(db.ManagementTasks.AsNoTracking(), scope, x => x.BranchId).CountAsync(x => !x.IsCompleted, token), outstandingReceivables = await invoices.SumAsync(x => (decimal?)x.BalanceDue, token) ?? 0, assetCount, rentedAssets = rented, utilizationPercent = assetCount == 0 ? 0 : Math.Round(rented * 100m / assetCount, 1), recordedRentalDays = rentalDays, recordedRevenue = revenue });
    }

    [HttpGet("workspace")]
    public async Task<ActionResult> Workspace(CancellationToken token)
    {
        var scope = await Scope(); if (scope is null) return Forbid();
        return Ok(new {
            quotes = await Scoped(db.SalesQuotes.AsNoTracking(), scope, x => x.BranchId).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(token),
            dispatches = await Scoped(db.DispatchJobs.AsNoTracking(), scope, x => x.BranchId).OrderBy(x => x.ScheduledAt).Take(100).ToListAsync(token),
            transfers = await TransferScope(scope).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(token),
            pricing = await ScopedNullable(db.PricingRules.AsNoTracking(), scope, x => x.BranchId).OrderBy(x => x.Name).ToListAsync(token),
            approvals = await Scoped(db.ApprovalRequests.AsNoTracking(), scope, x => x.BranchId).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(token),
            parts = await Scoped(db.InventoryParts.AsNoTracking(), scope, x => x.BranchId).OrderBy(x => x.Name).ToListAsync(token),
            purchaseOrders = await Scoped(db.PurchaseOrders.AsNoTracking(), scope, x => x.BranchId).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(token),
            cases = await Scoped(db.CustomerCases.AsNoTracking(), scope, x => x.BranchId).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(token),
            tasks = await ScopedNullable(db.ManagementTasks.AsNoTracking(), scope, x => x.BranchId).Where(x => !x.IsCompleted).OrderBy(x => x.DueAt).Take(100).ToListAsync(token),
            telematics = await db.TelematicsSnapshots.AsNoTracking().Where(x => db.Assets.Any(a => a.Id == x.AssetId && (scope.IsAdministrator || a.BranchId == scope.BranchId))).OrderByDescending(x => x.RecordedAt).Take(100).ToListAsync(token),
            corporateAccounts = await db.CorporateAccounts.AsNoTracking().OrderBy(x => x.LegalName).Take(100).ToListAsync(token),
        });
    }

    [HttpPost("quotes")]
    public async Task<ActionResult> CreateQuote(QuoteRequest request, CancellationToken token)
    {
        var scope = await ScopeFor(request.BranchId); if (scope is null) return Forbid(); var subtotal = request.Lines.Sum(x => x.Quantity * x.Rate); var taxable = Math.Max(0, subtotal - request.Discount); var quote = new SalesQuote { QuoteNumber = Number("QT"), CustomerId = request.CustomerId, BranchId = request.BranchId, AssignedUserId = scope.UserId, ValidUntil = request.ValidUntil, JobSite = Clean(request.JobSite), PurchaseOrderNumber = Clean(request.PurchaseOrderNumber), Subtotal = subtotal, Discount = request.Discount, Tax = taxable * request.TaxRate / 100m, Total = taxable * (1 + request.TaxRate / 100m), LineItemsJson = JsonSerializer.Serialize(request.Lines) }; db.SalesQuotes.Add(quote); await SaveAudit(scope, "Sales quote created", quote.Id, quote.QuoteNumber, request.BranchId, token); return Ok(quote);
    }

    [HttpPost("corporate-accounts")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> SaveCorporateAccount(CorporateAccountRequest request, CancellationToken token)
    {
        var scope = await Scope(); if (scope is null) return Forbid(); var account = await db.CorporateAccounts.FirstOrDefaultAsync(x => x.CustomerId == request.CustomerId, token) ?? new CorporateAccount { CustomerId = request.CustomerId, LegalName = request.LegalName.Trim() }; account.LegalName = request.LegalName.Trim(); account.TaxIdentificationNumber = Clean(request.TaxIdentificationNumber); account.CreditLimit = request.CreditLimit; account.PaymentTermsDays = request.PaymentTermsDays; account.PurchaseOrderRequired = request.PurchaseOrderRequired; account.CreditHold = request.CreditHold; account.BillingContactJson = JsonSerializer.Serialize(request.BillingContact); account.AuthorizedContactsJson = JsonSerializer.Serialize(request.AuthorizedContacts ?? []); account.JobSitesJson = JsonSerializer.Serialize(request.JobSites ?? []); account.ContractPricingJson = JsonSerializer.Serialize(request.ContractPricing ?? new { }); if (db.Entry(account).State == EntityState.Detached) db.CorporateAccounts.Add(account); await SaveAudit(scope, "Corporate account updated", account.Id, account.LegalName, null, token); return Ok(account);
    }

    [HttpPost("dispatches")]
    public async Task<ActionResult> CreateDispatch(DispatchRequest request, CancellationToken token) { var scope = await ScopeFor(request.BranchId); if (scope is null) return Forbid(); var job = new DispatchJob { DispatchNumber = Number("DSP"), BookingId = request.BookingId, BranchId = request.BranchId, Type = request.Type, ScheduledAt = request.ScheduledAt, Address = Clean(request.Address), AssignedDriver = Clean(request.AssignedDriver), TransportVehicle = Clean(request.TransportVehicle), DeliveryCharge = request.DeliveryCharge }; db.DispatchJobs.Add(job); await SaveAudit(scope, "Dispatch job created", job.Id, job.DispatchNumber, job.BranchId, token); return Ok(job); }

    [HttpPost("transfers")]
    public async Task<ActionResult> CreateTransfer(TransferRequest request, CancellationToken token) { var scope = await ScopeFor(request.FromBranchId); if (scope is null) return Forbid(); if (!scope.IsAdministrator && request.ToBranchId == request.FromBranchId) return BadRequest(); var transfer = new AssetTransfer { TransferNumber = Number("TRF"), AssetId = request.AssetId, FromBranchId = request.FromBranchId, ToBranchId = request.ToBranchId, Reason = request.Reason.Trim(), RequestedByUserId = scope.UserId, TransferCost = request.TransferCost }; db.AssetTransfers.Add(transfer); await SaveAudit(scope, "Asset transfer requested", transfer.Id, transfer.TransferNumber, request.FromBranchId, token); return Ok(transfer); }

    [HttpPost("pricing")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> CreatePricing(PricingRequest request, CancellationToken token) { var scope = request.BranchId.HasValue ? await ScopeFor(request.BranchId.Value) : await Scope(); if (scope is null || (!scope.IsAdministrator && !request.BranchId.HasValue)) return Forbid(); var rule = new PricingRule { Name = request.Name.Trim(), BranchId = request.BranchId, CustomerId = request.CustomerId, AssetType = Clean(request.AssetType), Period = request.Period, Rate = request.Rate, IncludedUsage = request.IncludedUsage, ExcessUsageRate = request.ExcessUsageRate, MinimumDuration = request.MinimumDuration, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo }; db.PricingRules.Add(rule); await SaveAudit(scope, "Pricing rule created", rule.Id, rule.Name, request.BranchId, token); return Ok(rule); }

    [HttpPost("approvals")]
    public async Task<ActionResult> CreateApproval(ApprovalRequestDto request, CancellationToken token) { var scope = await ScopeFor(request.BranchId); if (scope is null) return Forbid(); var approval = new ApprovalRequest { RequestNumber = Number("APR"), BranchId = request.BranchId, Type = request.Type, EntityType = request.EntityType.Trim(), EntityId = request.EntityId, Amount = request.Amount, Reason = request.Reason.Trim(), RequestedByUserId = scope.UserId }; db.ApprovalRequests.Add(approval); await SaveAudit(scope, "Approval requested", approval.Id, approval.RequestNumber, approval.BranchId, token); return Ok(approval); }

    [HttpPost("approvals/{id:guid}/decision")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> DecideApproval(Guid id, DecisionRequest request, CancellationToken token) { var item = await db.ApprovalRequests.FirstOrDefaultAsync(x => x.Id == id, token); if (item is null) return NotFound(); var scope = await ScopeFor(item.BranchId); if (scope is null) return Forbid(); item.Status = request.Approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected; item.DecidedByUserId = scope.UserId; item.DecisionNote = Clean(request.Note); item.DecidedAt = DateTimeOffset.UtcNow; await SaveAudit(scope, "Approval decided", item.Id, $"{item.RequestNumber}: {item.Status}", item.BranchId, token); return Ok(item); }

    [HttpPost("parts")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> CreatePart(PartRequest request, CancellationToken token) { var scope = await ScopeFor(request.BranchId); if (scope is null) return Forbid(); var part = new InventoryPart { PartNumber = request.PartNumber.Trim(), Name = request.Name.Trim(), BranchId = request.BranchId, Supplier = Clean(request.Supplier), UnitCost = request.UnitCost, QuantityOnHand = request.QuantityOnHand, ReorderLevel = request.ReorderLevel }; db.InventoryParts.Add(part); await SaveAudit(scope, "Inventory part created", part.Id, part.PartNumber, part.BranchId, token); return Ok(part); }

    [HttpPost("purchase-orders")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> CreatePurchaseOrder(PurchaseOrderRequest request, CancellationToken token) { var scope = await ScopeFor(request.BranchId); if (scope is null) return Forbid(); var order = new PurchaseOrder { PurchaseOrderNumber = Number("PO"), BranchId = request.BranchId, Supplier = request.Supplier.Trim(), Status = request.Total >= 1000 ? PurchaseOrderStatus.PendingApproval : PurchaseOrderStatus.Approved, Total = request.Total, LinesJson = JsonSerializer.Serialize(request.Lines), RequestedByUserId = scope.UserId }; db.PurchaseOrders.Add(order); if (order.Status == PurchaseOrderStatus.PendingApproval) db.ApprovalRequests.Add(new ApprovalRequest { RequestNumber = Number("APR"), BranchId = request.BranchId, Type = ApprovalType.PurchaseOrder, EntityType = nameof(PurchaseOrder), EntityId = order.Id, Amount = order.Total, Reason = $"Purchase order {order.PurchaseOrderNumber}", RequestedByUserId = scope.UserId }); await SaveAudit(scope, "Purchase order created", order.Id, order.PurchaseOrderNumber, order.BranchId, token); return Ok(order); }

    [HttpPost("cases")]
    public async Task<ActionResult> CreateCase(CaseRequest request, CancellationToken token) { var scope = await ScopeFor(request.BranchId); if (scope is null) return Forbid(); var item = new CustomerCase { CaseNumber = Number("CASE"), CustomerId = request.CustomerId, BranchId = request.BranchId, Type = request.Type, Priority = request.Priority, Subject = request.Subject.Trim(), Description = request.Description.Trim(), AssignedUserId = request.AssignedUserId, DueAt = request.DueAt }; db.CustomerCases.Add(item); await SaveAudit(scope, "Customer case created", item.Id, item.CaseNumber, item.BranchId, token); return Ok(item); }

    [HttpPost("tasks")]
    public async Task<ActionResult> CreateTask(TaskRequest request, CancellationToken token) { var scope = request.BranchId.HasValue ? await ScopeFor(request.BranchId.Value) : await Scope(); if (scope is null) return Forbid(); var item = new ManagementTask { BranchId = request.BranchId, Category = request.Category, Priority = request.Priority, Title = request.Title.Trim(), Description = Clean(request.Description), DueAt = request.DueAt, AssignedUserId = request.AssignedUserId }; db.ManagementTasks.Add(item); await SaveAudit(scope, "Management task created", item.Id, item.Title, item.BranchId, token); return Ok(item); }

    [HttpPost("telematics")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> RecordTelematics(TelematicsRequest request, CancellationToken token) { var asset = await db.Assets.FirstOrDefaultAsync(x => x.Id == request.AssetId, token); if (asset is null) return NotFound(); var scope = await ScopeFor(asset.BranchId); if (scope is null) return Forbid(); var snapshot = new TelematicsSnapshot { AssetId = request.AssetId, Provider = request.Provider.Trim(), RecordedAt = request.RecordedAt, Latitude = request.Latitude, Longitude = request.Longitude, Odometer = request.Odometer, EngineHours = request.EngineHours, FuelPercent = request.FuelPercent, FaultCodesJson = JsonSerializer.Serialize(request.FaultCodes ?? []), UnauthorizedMovement = request.UnauthorizedMovement }; db.TelematicsSnapshots.Add(snapshot); if (snapshot.UnauthorizedMovement) db.ManagementTasks.Add(new ManagementTask { BranchId = asset.BranchId, Category = TaskCategory.OverdueRental, Priority = TaskPriority.Critical, Title = $"Unauthorized movement: {asset.AssetNumber}", DueAt = DateTimeOffset.UtcNow, SourceEntityType = nameof(Asset), SourceEntityId = asset.Id }); await db.SaveChangesAsync(token); return Ok(snapshot); }

    [HttpPatch("quotes/{id:guid}/status")]
    public async Task<ActionResult> SetQuoteStatus(Guid id, QuoteStatusRequest request, CancellationToken token) { var item = await db.SalesQuotes.FirstOrDefaultAsync(x => x.Id == id, token); if (item is null) return NotFound(); var scope = await ScopeFor(item.BranchId); if (scope is null) return Forbid(); item.Status = request.Status; item.LostReason = request.Status == QuoteStatus.Rejected ? Clean(request.Note) : null; item.UpdatedAt = DateTimeOffset.UtcNow; await SaveAudit(scope, "Quote status changed", item.Id, $"{item.QuoteNumber}: {item.Status}", item.BranchId, token); return Ok(item); }

    [HttpPatch("dispatches/{id:guid}/status")]
    public async Task<ActionResult> SetDispatchStatus(Guid id, DispatchStatusRequest request, CancellationToken token) { var item = await db.DispatchJobs.FirstOrDefaultAsync(x => x.Id == id, token); if (item is null) return NotFound(); var scope = await ScopeFor(item.BranchId); if (scope is null) return Forbid(); item.Status = request.Status; item.ProofJson = request.Status == DispatchStatus.Completed ? JsonSerializer.Serialize(new { request.SignatureName, request.Notes, request.EvidenceDataUrls, completedAt = DateTimeOffset.UtcNow }) : item.ProofJson; item.UpdatedAt = DateTimeOffset.UtcNow; await SaveAudit(scope, "Dispatch status changed", item.Id, $"{item.DispatchNumber}: {item.Status}", item.BranchId, token); return Ok(item); }

    [HttpPatch("transfers/{id:guid}/status")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> SetTransferStatus(Guid id, TransferStatusRequest request, CancellationToken token) { var item = await db.AssetTransfers.FirstOrDefaultAsync(x => x.Id == id, token); if (item is null) return NotFound(); var scope = await Scope(); if (scope is null || (!scope.IsAdministrator && item.FromBranchId != scope.BranchId && item.ToBranchId != scope.BranchId)) return Forbid(); var valid = (item.Status, request.Status) switch { (TransferStatus.Requested, TransferStatus.Approved) => true, (TransferStatus.Approved, TransferStatus.InTransit) => true, (TransferStatus.InTransit, TransferStatus.Received) => true, (TransferStatus.Received, TransferStatus.Inspected) => true, (_, TransferStatus.Cancelled) => true, _ => false }; if (!valid) return BadRequest(new { message = $"A {item.Status} transfer cannot change to {request.Status}." }); item.Status = request.Status; item.InspectionJson = JsonSerializer.Serialize(new { request.MeterReading, request.ConditionNotes, request.DamageNotes, request.EvidenceDataUrls }); if (request.Status == TransferStatus.Approved) item.ApprovedByUserId = scope.UserId; if (request.Status == TransferStatus.InTransit) { item.DepartedAt = DateTimeOffset.UtcNow; item.DepartureMeter = request.MeterReading; } if (request.Status == TransferStatus.Received) { item.ReceivedAt = DateTimeOffset.UtcNow; item.ArrivalMeter = request.MeterReading; } if (request.Status == TransferStatus.Inspected) { var asset = await db.Assets.FindAsync([item.AssetId], token); if (asset is not null) asset.BranchId = item.ToBranchId; } await SaveAudit(scope, "Asset transfer status changed", item.Id, $"{item.TransferNumber}: {item.Status}", item.ToBranchId, token); return Ok(item); }

    [HttpGet("assets/{assetId:guid}/qr")]
    public async Task<ActionResult> AssetQr(Guid assetId, CancellationToken token) { var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == assetId, token); if (asset is null) return NotFound(); var scope = await ScopeFor(asset.BranchId); if (scope is null) return Forbid(); var scanValue = $"CREMS:ASSET:{asset.Id}"; return Ok(new { asset.Id, asset.AssetNumber, asset.Name, scanValue, staffUrl = $"/staff/scan?code={Uri.EscapeDataString(scanValue)}" }); }

    [HttpPost("documents")]
    public async Task<ActionResult> RegisterDocument(DocumentRequest request, CancellationToken token) { var scope = request.BranchId.HasValue ? await ScopeFor(request.BranchId.Value) : await Scope(); if (scope is null) return Forbid(); var document = new DocumentRecord { DocumentNumber = Number("DOC"), EntityType = request.EntityType.Trim(), EntityId = request.EntityId, BranchId = request.BranchId, Type = request.Type.Trim(), FileName = request.FileName.Trim(), StoragePath = request.StoragePath.Trim(), ExpiresOn = request.ExpiresOn, ContentHash = Clean(request.ContentHash) }; db.DocumentRecords.Add(document); if (document.ExpiresOn.HasValue) db.ManagementTasks.Add(new ManagementTask { BranchId = document.BranchId, Category = TaskCategory.ExpiringDocument, Priority = TaskPriority.Normal, Title = $"Review expiring {document.Type}: {document.FileName}", DueAt = document.ExpiresOn.Value.ToDateTime(TimeOnly.MinValue), SourceEntityType = nameof(DocumentRecord), SourceEntityId = document.Id }); await SaveAudit(scope, "Document registered", document.Id, document.DocumentNumber, document.BranchId, token); return Ok(document); }

    [HttpPost("refresh-alerts")]
    public async Task<ActionResult> RefreshAlerts(CancellationToken token) { var scope = await Scope(); if (scope is null) return Forbid(); var now = DateTimeOffset.UtcNow; var overdue = await Scoped(db.Bookings, scope, x => x.BranchId).Include(x => x.Items).Where(x => x.Status == BookingStatus.ConvertedToRental && x.Items.Any(i => i.EndAt < now)).ToListAsync(token); var created = 0; foreach (var booking in overdue) { if (await db.ManagementTasks.AnyAsync(x => !x.IsCompleted && x.SourceEntityType == nameof(Booking) && x.SourceEntityId == booking.Id, token)) continue; db.ManagementTasks.Add(new ManagementTask { BranchId = booking.BranchId, Category = TaskCategory.OverdueRental, Priority = TaskPriority.Critical, Title = $"Overdue rental {booking.BookingNumber}", DueAt = now, SourceEntityType = nameof(Booking), SourceEntityId = booking.Id }); created++; } var lowStock = await Scoped(db.InventoryParts, scope, x => x.BranchId).Where(x => x.QuantityOnHand - x.QuantityAllocated <= x.ReorderLevel).ToListAsync(token); foreach (var part in lowStock) { if (await db.ManagementTasks.AnyAsync(x => !x.IsCompleted && x.SourceEntityType == nameof(InventoryPart) && x.SourceEntityId == part.Id, token)) continue; db.ManagementTasks.Add(new ManagementTask { BranchId = part.BranchId, Category = TaskCategory.LowStock, Priority = TaskPriority.High, Title = $"Reorder {part.PartNumber} — {part.Name}", DueAt = now.AddDays(1), SourceEntityType = nameof(InventoryPart), SourceEntityId = part.Id }); created++; } await db.SaveChangesAsync(token); return Ok(new { created }); }

    private async Task<StaffDataScope?> Scope() => await staffScope.GetAsync(User);
    private async Task<StaffDataScope?> ScopeFor(Guid branchId) { var scope = await Scope(); return scope is not null && scope.HasBranchAccess(branchId) ? scope : null; }
    private IQueryable<T> Scoped<T>(IQueryable<T> query, StaffDataScope scope, System.Linq.Expressions.Expression<Func<T, Guid>> branch) => scope.IsAdministrator ? query : query.Where(BuildEqual(branch, scope.BranchId!.Value));
    private IQueryable<T> ScopedNullable<T>(IQueryable<T> query, StaffDataScope scope, System.Linq.Expressions.Expression<Func<T, Guid?>> branch) => scope.IsAdministrator ? query : query.Where(BuildEqual(branch, (Guid?)scope.BranchId));
    private IQueryable<AssetTransfer> TransferScope(StaffDataScope scope) => scope.IsAdministrator ? db.AssetTransfers.AsNoTracking() : db.AssetTransfers.AsNoTracking().Where(x => x.FromBranchId == scope.BranchId || x.ToBranchId == scope.BranchId);
    private static System.Linq.Expressions.Expression<Func<T, bool>> BuildEqual<T, TValue>(System.Linq.Expressions.Expression<Func<T, TValue>> selector, TValue value) { var body = System.Linq.Expressions.Expression.Equal(selector.Body, System.Linq.Expressions.Expression.Constant(value, typeof(TValue))); return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, selector.Parameters); }
    private async Task SaveAudit(StaffDataScope scope, string action, Guid id, string summary, Guid? branch, CancellationToken token) { AuditWriter.Record(db, scope, action, "CorporateOperation", id, summary, branch); await db.SaveChangesAsync(token); }
    private static string Number(string prefix) => $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record QuoteLine(string Description, decimal Quantity, decimal Rate);
public sealed record QuoteRequest(Guid CustomerId, Guid BranchId, DateTimeOffset ValidUntil, decimal Discount, decimal TaxRate, string? JobSite, string? PurchaseOrderNumber, IReadOnlyList<QuoteLine> Lines);
public sealed record CorporateAccountRequest(Guid CustomerId, string LegalName, string? TaxIdentificationNumber, decimal CreditLimit, int PaymentTermsDays, bool PurchaseOrderRequired, bool CreditHold, object? BillingContact, IReadOnlyList<object>? AuthorizedContacts, IReadOnlyList<object>? JobSites, object? ContractPricing);
public sealed record DispatchRequest(Guid BookingId, Guid BranchId, DispatchType Type, DateTimeOffset ScheduledAt, string? Address, string? AssignedDriver, string? TransportVehicle, decimal DeliveryCharge);
public sealed record TransferRequest(Guid AssetId, Guid FromBranchId, Guid ToBranchId, string Reason, decimal TransferCost);
public sealed record PricingRequest(string Name, Guid? BranchId, Guid? CustomerId, string? AssetType, RatePeriod Period, decimal Rate, decimal IncludedUsage, decimal ExcessUsageRate, int MinimumDuration, DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo);
public sealed record ApprovalRequestDto(Guid BranchId, ApprovalType Type, string EntityType, Guid EntityId, decimal Amount, string Reason);
public sealed record DecisionRequest(bool Approved, string? Note);
public sealed record PartRequest(string PartNumber, string Name, Guid BranchId, string? Supplier, decimal UnitCost, int QuantityOnHand, int ReorderLevel);
public sealed record PurchaseOrderLine(string PartNumber, string Description, int Quantity, decimal UnitCost);
public sealed record PurchaseOrderRequest(Guid BranchId, string Supplier, decimal Total, IReadOnlyList<PurchaseOrderLine> Lines);
public sealed record CaseRequest(Guid CustomerId, Guid BranchId, CaseType Type, CasePriority Priority, string Subject, string Description, Guid? AssignedUserId, DateTimeOffset DueAt);
public sealed record TaskRequest(Guid? BranchId, TaskCategory Category, TaskPriority Priority, string Title, string? Description, DateTimeOffset DueAt, Guid? AssignedUserId);
public sealed record TelematicsRequest(Guid AssetId, string Provider, DateTimeOffset RecordedAt, decimal? Latitude, decimal? Longitude, decimal? Odometer, decimal? EngineHours, decimal? FuelPercent, IReadOnlyList<string>? FaultCodes, bool UnauthorizedMovement);
public sealed record QuoteStatusRequest(QuoteStatus Status, string? Note);
public sealed record DispatchStatusRequest(DispatchStatus Status, string? SignatureName, string? Notes, IReadOnlyList<string>? EvidenceDataUrls);
public sealed record TransferStatusRequest(TransferStatus Status, decimal? MeterReading, string? ConditionNotes, string? DamageNotes, IReadOnlyList<string>? EvidenceDataUrls);
public sealed record DocumentRequest(string EntityType, Guid EntityId, Guid? BranchId, string Type, string FileName, string StoragePath, DateOnly? ExpiresOn, string? ContentHash);
