using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Encodings.Web;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/corporate-operations")]
[Authorize(Policy = SystemPolicies.StaffPortal)]
public sealed class CorporateOperationsController(ApplicationDbContext db, CurrentStaffScope staffScope, IEmailQueue emailQueue, IAuthorizationService authorization) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult> Overview(CancellationToken token)
    {
        var scope = await Scope(); if (scope is null) return Forbid(); var now = DateTimeOffset.UtcNow;
        var bookings = BookingScope(scope);
        var assets = db.Assets.AsNoTracking().Where(x => scope.IsAdministrator || scope.BranchIds.Contains(x.BranchId) && x.DivisionId.HasValue && scope.DivisionIds.Contains(x.DivisionId.Value));
        var rentalDays = await bookings.Where(x => x.Status == BookingStatus.ConvertedToRental).SelectMany(x => x.Items).SumAsync(x => (decimal?)EF.Functions.DateDiffDay(x.StartAt, x.EndAt), token) ?? 0;
        var revenue = await bookings.Where(x => x.Status == BookingStatus.Completed || x.Status == BookingStatus.ConvertedToRental).SelectMany(x => x.Items).SumAsync(x => (decimal?)(x.DailyRate * (EF.Functions.DateDiffDay(x.StartAt, x.EndAt) < 1 ? 1 : EF.Functions.DateDiffDay(x.StartAt, x.EndAt))), token) ?? 0;
        var assetCount = await assets.CountAsync(x => x.IsActive, token); var rented = await assets.CountAsync(x => x.Status == AssetStatus.Rented, token);
        var invoices = db.RentalInvoices.AsNoTracking().Where(x => bookings.Any(b => b.Id == x.BookingId));
        return Ok(new { activeRentals = await bookings.CountAsync(x => x.Status == BookingStatus.ConvertedToRental, token), overdueRentals = await bookings.CountAsync(x => x.Status == BookingStatus.ConvertedToRental && x.Items.Any(i => i.EndAt < now), token), pendingQuotes = await QuoteScope(scope).CountAsync(x => x.Status == QuoteStatus.Draft || x.Status == QuoteStatus.Sent || x.Status == QuoteStatus.Negotiating, token), todayDispatches = await Scoped(db.DispatchJobs.AsNoTracking(), scope, x => x.BranchId).CountAsync(x => x.ScheduledAt.Date == now.Date && x.Status != DispatchStatus.Completed && x.Status != DispatchStatus.Cancelled, token), pendingApprovals = await ApprovalScope(scope).CountAsync(x => x.Status == ApprovalStatus.Pending, token), lowStockParts = await Scoped(db.InventoryParts.AsNoTracking(), scope, x => x.BranchId).CountAsync(x => x.QuantityOnHand - x.QuantityAllocated <= x.ReorderLevel, token), openCases = await Scoped(db.CustomerCases.AsNoTracking(), scope, x => x.BranchId).CountAsync(x => x.Status != CaseStatus.Resolved && x.Status != CaseStatus.Closed, token), activeTasks = await ScopedNullable(db.ManagementTasks.AsNoTracking(), scope, x => x.BranchId).CountAsync(x => !x.IsCompleted, token), outstandingReceivables = await invoices.SumAsync(x => (decimal?)x.BalanceDue, token) ?? 0, assetCount, rentedAssets = rented, utilizationPercent = assetCount == 0 ? 0 : Math.Round(rented * 100m / assetCount, 1), recordedRentalDays = rentalDays, recordedRevenue = revenue });
    }

    [HttpGet("workspace")]
    public async Task<ActionResult> Workspace(CancellationToken token)
    {
        var scope = await Scope(); if (scope is null) return Forbid();
        var bookings = BookingScope(scope);
        return Ok(new {
            quotes = await QuoteScope(scope).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(token),
            dispatches = await Scoped(db.DispatchJobs.AsNoTracking(), scope, x => x.BranchId).Where(x => scope.IsAdministrator || bookings.Any(b => b.Id == x.BookingId)).OrderBy(x => x.ScheduledAt).Take(100).ToListAsync(token),
            transfers = await TransferScope(scope).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(token),
            pricing = await db.PricingRules.AsNoTracking().Where(x => scope.IsAdministrator || (!x.BranchId.HasValue || scope.BranchIds.Contains(x.BranchId.Value)) && (!x.DivisionId.HasValue || scope.DivisionIds.Contains(x.DivisionId.Value))).OrderBy(x => x.Name).ToListAsync(token),
            approvals = await ApprovalScope(scope).OrderByDescending(x => x.CreatedAt).Take(100).Select(x => new {
                x.Id, x.RequestNumber, x.Status, x.Type, x.EntityType, x.Amount, x.Reason, x.CreatedAt, x.CurrentStage, x.TotalStages,
                canDecide = x.Status == ApprovalStatus.Pending && x.StageDecisions.Any(s => s.StageNumber == x.CurrentStage && s.Status == ApprovalStatus.Pending &&
                    s.AssignedRole == SystemRoles.BranchManager && User.IsInRole(SystemRoles.BranchManager) &&
                    (x.EntityType == nameof(Booking) || x.EntityType == nameof(SalesQuote) || x.RequestedByUserId != scope.UserId && (!s.AssignedUserId.HasValue || s.AssignedUserId == scope.UserId))),
                stageDecisions = x.StageDecisions.OrderBy(s => s.StageNumber).Select(s => new { s.StageNumber, s.StageName, s.AssignedRole, s.Status, s.DecisionNote, s.DecidedAt })
            }).ToListAsync(token),
            parts = await Scoped(db.InventoryParts.AsNoTracking(), scope, x => x.BranchId).OrderBy(x => x.Name).ToListAsync(token),
            purchaseOrders = await Scoped(db.PurchaseOrders.AsNoTracking(), scope, x => x.BranchId).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(token),
            cases = await Scoped(db.CustomerCases.AsNoTracking(), scope, x => x.BranchId).Where(x => scope.IsAdministrator || x.AssignedUserId == scope.UserId || bookings.Any(b => b.CustomerId == x.CustomerId)).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(token),
            tasks = await ScopedNullable(db.ManagementTasks.AsNoTracking(), scope, x => x.BranchId).Where(x => !x.IsCompleted && (scope.IsAdministrator || x.AssignedUserId == scope.UserId || x.SourceEntityType == nameof(Booking) && bookings.Any(b => b.Id == x.SourceEntityId))).OrderBy(x => x.DueAt).Take(100).ToListAsync(token),
            telematics = await db.TelematicsSnapshots.AsNoTracking().Where(x => db.Assets.Any(a => a.Id == x.AssetId && (scope.IsAdministrator || scope.BranchIds.Contains(a.BranchId) && a.DivisionId.HasValue && scope.DivisionIds.Contains(a.DivisionId.Value)))).OrderByDescending(x => x.RecordedAt).Take(100).ToListAsync(token),
            corporateAccounts = await db.CorporateAccounts.AsNoTracking().Where(x => scope.IsAdministrator || bookings.Any(b => b.CustomerId == x.CustomerId)).OrderBy(x => x.LegalName).Take(100).ToListAsync(token),
        });
    }

    [HttpPost("quotes")]
    public async Task<ActionResult> CreateQuote(QuoteRequest request, CancellationToken token)
    {
        var scope = await ScopeFor(request.BranchId); if (scope is null) return Forbid();
        var divisionId = scope.IsAdministrator ? request.DivisionId : scope.DivisionId;
        if (!divisionId.HasValue || !await db.BranchDivisions.AnyAsync(x => x.BranchId == request.BranchId && x.DivisionId == divisionId && x.IsActive, token)) return Validation("divisionId", "Select a division operating at this branch.");
        if (!await db.Customers.AnyAsync(x => x.Id == request.CustomerId && x.IsActive && !x.IsBlocked, token)) return Validation("customerId", "Select an active eligible customer.");
        if (request.ValidUntil <= DateTimeOffset.UtcNow || request.Lines.Count == 0 || request.Lines.Any(x => string.IsNullOrWhiteSpace(x.Description) || x.Quantity <= 0 || x.Rate < 0 || x.CostRate < 0) || request.Discount < 0 || request.TaxRate is < 0 or > 100) return Validation("quote", "Enter valid lines, units, selling rates, cost rates, discount, tax and a future expiry date.");
        var totals = QuotePolicy.Calculate(request.Lines.Select(x => (x.Quantity, x.Rate)), request.Discount, request.TaxRate);
        if (request.Discount > totals.Subtotal) return Validation("discount", "Discount cannot exceed the quote subtotal.");
        var quote = new SalesQuote { QuoteNumber = Number("QT"), CustomerId = request.CustomerId, BranchId = request.BranchId, DivisionId = divisionId, AssignedUserId = scope.UserId, ValidUntil = request.ValidUntil, JobSite = Clean(request.JobSite), PurchaseOrderNumber = Clean(request.PurchaseOrderNumber), Subtotal = totals.Subtotal, Discount = request.Discount, Tax = totals.Tax, Total = totals.Total, LineItemsJson = JsonSerializer.Serialize(request.Lines) };
        db.SalesQuotes.Add(quote);
        if (request.Discount > 0) { var approval = new ApprovalRequest { RequestNumber = Number("APR"), BranchId = request.BranchId, Type = ApprovalType.Discount, EntityType = nameof(SalesQuote), EntityId = quote.Id, Amount = request.Discount, Reason = $"Discount approval for {quote.QuoteNumber}", RequestedByUserId = scope.UserId }; await ApprovalWorkflowService.ConfigureAsync(db, approval, divisionId, token); db.ApprovalRequests.Add(approval); }
        await SaveAudit(scope, "Sales quote created", quote.Id, quote.QuoteNumber, request.BranchId, token); return Ok(quote);
    }

    [HttpPost("corporate-accounts")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> SaveCorporateAccount(CorporateAccountRequest request, CancellationToken token)
    {
        var scope = await Scope(); if (scope is null) return Forbid(); var account = await db.CorporateAccounts.FirstOrDefaultAsync(x => x.CustomerId == request.CustomerId, token) ?? new CorporateAccount { CustomerId = request.CustomerId, LegalName = request.LegalName.Trim() }; account.LegalName = request.LegalName.Trim(); account.TaxIdentificationNumber = Clean(request.TaxIdentificationNumber); account.CreditLimit = request.CreditLimit; account.PaymentTermsDays = request.PaymentTermsDays; account.PurchaseOrderRequired = request.PurchaseOrderRequired; account.CreditHold = request.CreditHold; account.BillingContactJson = JsonSerializer.Serialize(request.BillingContact); account.AuthorizedContactsJson = JsonSerializer.Serialize(request.AuthorizedContacts ?? []); account.JobSitesJson = JsonSerializer.Serialize(request.JobSites ?? []); account.ContractPricingJson = JsonSerializer.Serialize(request.ContractPricing ?? new { }); if (db.Entry(account).State == EntityState.Detached) db.CorporateAccounts.Add(account); await SaveAudit(scope, "Corporate account updated", account.Id, account.LegalName, null, token); return Ok(account);
    }

    [HttpPost("dispatches")]
    public async Task<ActionResult> CreateDispatch(DispatchRequest request, CancellationToken token) { var scope = await ScopeFor(request.BranchId); if (scope is null) return Forbid(); var job = new DispatchJob { DispatchNumber = Number("DSP"), BookingId = request.BookingId, BranchId = request.BranchId, Type = request.Type, ScheduledAt = request.ScheduledAt, Address = Clean(request.Address), AssignedDriver = Clean(request.AssignedDriver), TransportVehicle = Clean(request.TransportVehicle), DeliveryZoneId = request.DeliveryZoneId, DistanceKilometres = request.DistanceKilometres, InternalTransportCost = request.InternalTransportCost, FailedDeliveryCharge = request.FailedDeliveryCharge, DeliveryCharge = request.DeliveryCharge }; db.DispatchJobs.Add(job); await SaveAudit(scope, "Dispatch job created", job.Id, job.DispatchNumber, job.BranchId, token); return Ok(job); }

    [HttpPost("transfers")]
    public async Task<ActionResult> CreateTransfer(TransferRequest request, CancellationToken token) { var scope = await ScopeFor(request.FromBranchId); if (scope is null) return Forbid(); if (!scope.IsAdministrator && request.ToBranchId == request.FromBranchId) return BadRequest(); var transfer = new AssetTransfer { TransferNumber = Number("TRF"), AssetId = request.AssetId, FromBranchId = request.FromBranchId, ToBranchId = request.ToBranchId, Reason = request.Reason.Trim(), RequestedByUserId = scope.UserId, TransferCost = request.TransferCost }; db.AssetTransfers.Add(transfer); await SaveAudit(scope, "Asset transfer requested", transfer.Id, transfer.TransferNumber, request.FromBranchId, token); return Ok(transfer); }

    [HttpPost("pricing")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> CreatePricing(PricingRequest request, CancellationToken token) { var scope = request.BranchId.HasValue ? await ScopeFor(request.BranchId.Value) : await Scope(); if (scope is null || (!scope.IsAdministrator && !request.BranchId.HasValue) || !scope.HasDivisionAccess(request.DivisionId)) return Forbid(); var rule = new PricingRule { Name = request.Name.Trim(), BranchId = request.BranchId, DivisionId = request.DivisionId, CustomerId = request.CustomerId, CustomerType = Clean(request.CustomerType), ChargeDefinitionId = request.ChargeDefinitionId, AssetType = Clean(request.AssetType), Period = request.Period, Rate = request.Rate, IncludedUsage = request.IncludedUsage, ExcessUsageRate = request.ExcessUsageRate, MinimumDuration = request.MinimumDuration, WeekendMultiplier = request.WeekendMultiplier, HolidayMultiplier = request.HolidayMultiplier, OvertimeMultiplier = request.OvertimeMultiplier, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo }; db.PricingRules.Add(rule); await SaveAudit(scope, "Pricing rule created", rule.Id, rule.Name, request.BranchId, token); return Ok(rule); }

    [HttpPost("approvals")]
    public async Task<ActionResult> CreateApproval(ApprovalRequestDto request, CancellationToken token) { var scope = await ScopeFor(request.BranchId); if (scope is null) return Forbid(); var approval = new ApprovalRequest { RequestNumber = Number("APR"), BranchId = request.BranchId, Type = request.Type, EntityType = request.EntityType.Trim(), EntityId = request.EntityId, Amount = request.Amount, Reason = request.Reason.Trim(), RequestedByUserId = scope.UserId }; await ApprovalWorkflowService.ConfigureAsync(db, approval, scope.DivisionId, token); db.ApprovalRequests.Add(approval); await SaveAudit(scope, "Approval requested", approval.Id, approval.RequestNumber, approval.BranchId, token); return Ok(approval); }

    [HttpPost("approvals/{id:guid}/decision")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> DecideApproval(Guid id, DecisionRequest request, CancellationToken token)
    {
        var item = await db.ApprovalRequests.Include(x => x.StageDecisions).FirstOrDefaultAsync(x => x.Id == id, token); if (item is null) return NotFound();
        var scope = await ScopeFor(item.BranchId); if (scope is null || !await ApprovalScope(scope).AnyAsync(x => x.Id == id, token)) return Forbid();
        if (item.Status != ApprovalStatus.Pending) return Validation("status", "Only a pending request can be decided.");
        if (!request.Approved && string.IsNullOrWhiteSpace(request.Note)) return Validation("note", "Enter a clear reason before rejecting this request.");
        var requiredPermission = item.Type == ApprovalType.Refund ? SystemPermissions.PaymentsRefund : SystemPermissions.RentalsApprove;
        if (!(await authorization.AuthorizeAsync(User, requiredPermission)).Succeeded) return Forbid();
        var stage = item.StageDecisions.FirstOrDefault(x => x.StageNumber == item.CurrentStage);
        if (stage is null) { stage = new ApprovalStageDecision { ApprovalRequestId = item.Id, StageNumber = item.CurrentStage, StageName = "Manager approval", AssignedRole = SystemRoles.BranchManager }; item.StageDecisions.Add(stage); }
        var designatedRentalManager = item.EntityType is nameof(Booking) or nameof(SalesQuote) &&
            stage.AssignedRole == SystemRoles.BranchManager && User.IsInRole(SystemRoles.BranchManager);
        if (item.RequestedByUserId == scope.UserId && !designatedRentalManager)
            return Validation("approver", "The requester cannot approve or reject their own request.");
        // Booking and quotation approvals are operationally assigned to the manager
        // of the request branch. Do not let an obsolete named-user assignment block
        // another active manager who has the same branch and role scope.
        if (!scope.IsAdministrator && !designatedRentalManager && stage.AssignedUserId.HasValue && stage.AssignedUserId != scope.UserId) return Forbid();
        if (!scope.IsAdministrator && !string.IsNullOrWhiteSpace(stage.AssignedRole) && !User.IsInRole(stage.AssignedRole)) return Forbid();
        stage.Status = request.Approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected; stage.DecidedByUserId = scope.UserId; stage.DecisionNote = Clean(request.Note); stage.DecidedAt = DateTimeOffset.UtcNow;
        if (!request.Approved) { item.Status = ApprovalStatus.Rejected; item.DecidedAt = stage.DecidedAt; item.DecidedByUserId = scope.UserId; item.DecisionNote = stage.DecisionNote; }
        else if (item.CurrentStage >= item.TotalStages) { item.Status = ApprovalStatus.Approved; item.DecidedAt = stage.DecidedAt; item.DecidedByUserId = scope.UserId; item.DecisionNote = stage.DecisionNote; }
        else item.CurrentStage++;
        if (item.EntityType == nameof(Booking) && item.Status is ApprovalStatus.Approved or ApprovalStatus.Rejected)
        {
            var booking = await db.Bookings.Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == item.EntityId, token);
            if (booking?.Customer?.Email is { Length: > 0 } email)
                db.RentalNotifications.Add(new RentalNotification { BookingId = booking.Id, Channel = NotificationChannel.Email, Recipient = email,
                    Subject = item.Status == ApprovalStatus.Approved ? $"Rental request {booking.BookingNumber} approved" : $"Rental request {booking.BookingNumber} requires attention",
                    Message = item.Status == ApprovalStatus.Approved ? "The required branch approval has been completed. Your rental request can now proceed to confirmation." : $"The approval was declined. Reason: {stage.DecisionNote ?? "No reason provided."}" });
        }
        await SaveAudit(scope, "Approval phase decided", item.Id, $"{item.RequestNumber}: phase {stage.StageNumber}/{item.TotalStages} {stage.Status}; request {item.Status}", item.BranchId, token); return Ok(new { item.Id, item.Status, item.CurrentStage, item.TotalStages });
    }

    [HttpPost("parts")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> CreatePart(PartRequest request, CancellationToken token) { var scope = await ScopeFor(request.BranchId); if (scope is null) return Forbid(); var part = new InventoryPart { PartNumber = request.PartNumber.Trim(), Name = request.Name.Trim(), BranchId = request.BranchId, Supplier = Clean(request.Supplier), UnitCost = request.UnitCost, QuantityOnHand = request.QuantityOnHand, ReorderLevel = request.ReorderLevel }; db.InventoryParts.Add(part); await SaveAudit(scope, "Inventory part created", part.Id, part.PartNumber, part.BranchId, token); return Ok(part); }

    [HttpPost("purchase-orders")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> CreatePurchaseOrder(PurchaseOrderRequest request, CancellationToken token) { var scope = await ScopeFor(request.BranchId); if (scope is null) return Forbid(); var order = new PurchaseOrder { PurchaseOrderNumber = Number("PO"), BranchId = request.BranchId, Supplier = request.Supplier.Trim(), Status = request.Total >= 1000 ? PurchaseOrderStatus.PendingApproval : PurchaseOrderStatus.Approved, Total = request.Total, LinesJson = JsonSerializer.Serialize(request.Lines), RequestedByUserId = scope.UserId }; db.PurchaseOrders.Add(order); if (order.Status == PurchaseOrderStatus.PendingApproval) { var approval = new ApprovalRequest { RequestNumber = Number("APR"), BranchId = request.BranchId, Type = ApprovalType.PurchaseOrder, EntityType = nameof(PurchaseOrder), EntityId = order.Id, Amount = order.Total, Reason = $"Purchase order {order.PurchaseOrderNumber}", RequestedByUserId = scope.UserId }; await ApprovalWorkflowService.ConfigureAsync(db, approval, scope.DivisionId, token); db.ApprovalRequests.Add(approval); } await SaveAudit(scope, "Purchase order created", order.Id, order.PurchaseOrderNumber, order.BranchId, token); return Ok(order); }

    [HttpPost("cases")]
    public async Task<ActionResult> CreateCase(CaseRequest request, CancellationToken token) { var scope = await ScopeFor(request.BranchId); if (scope is null) return Forbid(); var item = new CustomerCase { CaseNumber = Number("CASE"), CustomerId = request.CustomerId, BranchId = request.BranchId, Type = request.Type, Priority = request.Priority, Subject = request.Subject.Trim(), Description = request.Description.Trim(), AssignedUserId = request.AssignedUserId, DueAt = request.DueAt }; db.CustomerCases.Add(item); await SaveAudit(scope, "Customer case created", item.Id, item.CaseNumber, item.BranchId, token); return Ok(item); }

    [HttpPost("tasks")]
    public async Task<ActionResult> CreateTask(TaskRequest request, CancellationToken token) { var scope = request.BranchId.HasValue ? await ScopeFor(request.BranchId.Value) : await Scope(); if (scope is null) return Forbid(); var item = new ManagementTask { BranchId = request.BranchId, Category = request.Category, Priority = request.Priority, Title = request.Title.Trim(), Description = Clean(request.Description), DueAt = request.DueAt, AssignedUserId = request.AssignedUserId }; db.ManagementTasks.Add(item); await SaveAudit(scope, "Management task created", item.Id, item.Title, item.BranchId, token); return Ok(item); }

    [HttpPost("telematics")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> RecordTelematics(TelematicsRequest request, CancellationToken token) { var asset = await db.Assets.FirstOrDefaultAsync(x => x.Id == request.AssetId, token); if (asset is null) return NotFound(); var scope = await ScopeFor(asset.BranchId); if (scope is null) return Forbid(); var snapshot = new TelematicsSnapshot { AssetId = request.AssetId, Provider = request.Provider.Trim(), RecordedAt = request.RecordedAt, Latitude = request.Latitude, Longitude = request.Longitude, Odometer = request.Odometer, EngineHours = request.EngineHours, FuelPercent = request.FuelPercent, FaultCodesJson = JsonSerializer.Serialize(request.FaultCodes ?? []), UnauthorizedMovement = request.UnauthorizedMovement }; db.TelematicsSnapshots.Add(snapshot); if (snapshot.UnauthorizedMovement) db.ManagementTasks.Add(new ManagementTask { BranchId = asset.BranchId, Category = TaskCategory.OverdueRental, Priority = TaskPriority.Critical, Title = $"Unauthorized movement: {asset.AssetNumber}", DueAt = DateTimeOffset.UtcNow, SourceEntityType = nameof(Asset), SourceEntityId = asset.Id }); await db.SaveChangesAsync(token); return Ok(snapshot); }

    [HttpPatch("quotes/{id:guid}/status")]
    public async Task<ActionResult> SetQuoteStatus(Guid id, QuoteStatusRequest request, CancellationToken token) { var item = await db.SalesQuotes.FirstOrDefaultAsync(x => x.Id == id, token); if (item is null) return NotFound(); var scope = await ScopeFor(item.BranchId); if (scope is null || (!scope.IsAdministrator && item.DivisionId != scope.DivisionId)) return Forbid(); if (request.Status == QuoteStatus.Sent) return Validation("status", "Use Send to customer so the quote status and email delivery record stay synchronized."); if (!QuotePolicy.IsValidTransition(item.Status, request.Status)) return Validation("status", $"A {item.Status} quote cannot change to {request.Status}."); if (request.Status == QuoteStatus.Rejected && string.IsNullOrWhiteSpace(request.Note)) return Validation("note", "Record why the quote was rejected."); item.Status = request.Status; item.LostReason = request.Status == QuoteStatus.Rejected ? Clean(request.Note) : null; item.UpdatedAt = DateTimeOffset.UtcNow; await SaveAudit(scope, "Quote status changed", item.Id, $"{item.QuoteNumber}: {item.Status}", item.BranchId, token); return Ok(item); }

    [HttpPost("quotes/{id:guid}/revisions")]
    public async Task<ActionResult> ReviseQuote(Guid id, ReviseQuoteRequest request, CancellationToken token)
    {
        var item = await db.SalesQuotes.FirstOrDefaultAsync(x => x.Id == id, token); if (item is null) return NotFound(); var scope = await ScopeFor(item.BranchId); if (scope is null || (!scope.IsAdministrator && item.DivisionId != scope.DivisionId)) return Forbid();
        if (item.Status is QuoteStatus.Accepted or QuoteStatus.Converted or QuoteStatus.Expired) return Validation("status", "Accepted, converted or expired quotes cannot be revised.");
        if (request.Lines.Count == 0 || request.Lines.Any(x => string.IsNullOrWhiteSpace(x.Description) || x.Quantity <= 0 || x.Rate < 0 || x.CostRate < 0) || request.ValidUntil <= DateTimeOffset.UtcNow) return Validation("quote", "Enter valid revised lines and a future expiry date.");
        db.QuoteRevisions.Add(new QuoteRevision { SalesQuoteId = item.Id, Version = item.Version, SnapshotJson = JsonSerializer.Serialize(new { item.ValidUntil, item.Subtotal, item.Discount, item.Tax, item.Total, item.LineItemsJson }), ChangeReason = request.Reason.Trim(), ChangedByUserId = scope.UserId });
        var totals = QuotePolicy.Calculate(request.Lines.Select(x => (x.Quantity, x.Rate)), request.Discount, request.TaxRate); item.Version++; item.ValidUntil = request.ValidUntil; item.Discount = request.Discount; item.Subtotal = totals.Subtotal; item.Tax = totals.Tax; item.Total = totals.Total; item.LineItemsJson = JsonSerializer.Serialize(request.Lines); item.Status = QuoteStatus.Negotiating; item.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAudit(scope, "Sales quote revised", item.Id, $"{item.QuoteNumber} revised to version {item.Version}: {request.Reason.Trim()}", item.BranchId, token); return Ok(item);
    }

    [HttpPost("quotes/{id:guid}/send")]
    public async Task<ActionResult> SendQuote(Guid id, CancellationToken token)
    {
        var quote = await db.SalesQuotes.FirstOrDefaultAsync(x => x.Id == id, token); if (quote is null) return NotFound();
        var scope = await ScopeFor(quote.BranchId); if (scope is null || (!scope.IsAdministrator && quote.DivisionId != scope.DivisionId)) return Forbid();
        if (quote.Status is not (QuoteStatus.Draft or QuoteStatus.Sent or QuoteStatus.Negotiating)) return Validation("status", "Only a draft, sent or negotiating quote can be emailed.");
        if (quote.ValidUntil <= DateTimeOffset.UtcNow) return Validation("expiry", "Revise the expired quotation before sending it.");
        if (quote.ConvertedBookingId is Guid bookingId)
        {
            var booking = await db.Bookings.Include(x => x.Items).ThenInclude(x => x.Asset).Include(x => x.Charges).FirstOrDefaultAsync(x => x.Id == bookingId, token);
            if (booking is null || booking.Status != BookingStatus.Draft) return Validation("booking", "Only an open rental request can be quoted.");
            var match = await ApprovalWorkflowService.MatchBookingAsync(db, new(booking.BranchId, quote.DivisionId, quote.Total,
                booking.Items.Any(x => x.Asset != null && AssetCategoryPolicy.IsEquipment(x.Asset.Type)),
                booking.Items.Any(x => x.Asset!.PersonnelRequirement == PersonnelRequirement.Required) || booking.Charges.Any(x => x.Category == ChargeCategory.Operator || x.Category == ChargeCategory.Driver || x.Category == ChargeCategory.Labour),
                booking.Charges.Any(x => x.Description.ToLower().Contains("overtime"))), token);
            if (match is not null)
            {
                var approval = await db.ApprovalRequests.Where(x => x.EntityType == nameof(Booking) && x.EntityId == bookingId && x.WorkflowId == match.WorkflowId).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(token);
                if (approval?.Status == ApprovalStatus.Rejected) return Validation("approval", "Manager approval was declined. Revise the quotation before resubmitting.");
                if (approval?.Status != ApprovalStatus.Approved)
                {
                    if (approval?.Status != ApprovalStatus.Pending)
                    {
                        approval = new ApprovalRequest { RequestNumber = Number("APR"), BranchId = booking.BranchId, Type = ApprovalType.Booking, EntityType = nameof(Booking), EntityId = booking.Id, Amount = quote.Total, Reason = $"{quote.QuoteNumber} revision {quote.Version}: {match.Reason}", RequestedByUserId = scope.UserId };
                        ApprovalWorkflowService.ConfigureFromMatch(approval, match);
                        ApprovalWorkflowService.RecordRentalOfficerReview(approval, scope.UserId);
                        db.ApprovalRequests.Add(approval);
                        await db.SaveChangesAsync(token);
                    }
                    if (approval.Status == ApprovalStatus.Pending) return Accepted(new { outcome = "AwaitingApproval", approval.RequestNumber, message = "Submitted to the assigned branch manager. Send to the customer after approval." });
                }
            }
        }
        if (await db.ApprovalRequests.AnyAsync(x => x.EntityType == nameof(SalesQuote) && x.EntityId == quote.Id && x.Status == ApprovalStatus.Pending, token)) return Validation("approval", "The discount must be approved before this quote is sent.");
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == quote.CustomerId, token);
        if (customer is null || string.IsNullOrWhiteSpace(customer.Email)) return Validation("email", "The customer must have an email address before the quote can be sent.");
        var branch = await db.Branches.AsNoTracking().FirstAsync(x => x.Id == quote.BranchId, token);
        var division = quote.DivisionId.HasValue ? await db.Divisions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == quote.DivisionId, token) : null;
        var lines = JsonSerializer.Deserialize<IReadOnlyList<QuoteLine>>(quote.LineItemsJson ?? "[]") ?? [];
        var rows = string.Join("", lines.Select(x => $"<tr><td style=\"padding:9px;border-bottom:1px solid #ddd\">{HtmlEncoder.Default.Encode(x.Description)}</td><td style=\"padding:9px;text-align:right;border-bottom:1px solid #ddd\">{x.Quantity:0.##} {x.Unit.ToString().ToLowerInvariant()}</td><td style=\"padding:9px;text-align:right;border-bottom:1px solid #ddd\">{x.Rate:N2}</td><td style=\"padding:9px;text-align:right;border-bottom:1px solid #ddd\">{x.Quantity * x.Rate:N2}</td></tr>"));
        var content = $"<p>Dear {HtmlEncoder.Default.Encode(customer.Name)},</p><p>Please find quotation <strong>{quote.QuoteNumber}</strong> from {HtmlEncoder.Default.Encode(division?.Name ?? "Carpenters Fiji")}.</p><table role=\"presentation\" width=\"100%\" cellspacing=\"0\"><tr style=\"background:#f1f1ed\"><th align=\"left\" style=\"padding:9px\">Description</th><th align=\"right\">Qty</th><th align=\"right\">Rate (FJD)</th><th align=\"right\">Amount</th></tr>{rows}</table><table role=\"presentation\" width=\"100%\" style=\"margin-top:16px\"><tr><td>Subtotal</td><td align=\"right\">FJD {quote.Subtotal:N2}</td></tr><tr><td>Discount</td><td align=\"right\">FJD {quote.Discount:N2}</td></tr><tr><td>VAT</td><td align=\"right\">FJD {quote.Tax:N2}</td></tr><tr><td style=\"font-size:18px;font-weight:bold;padding-top:8px\">Total</td><td align=\"right\" style=\"font-size:18px;font-weight:bold;padding-top:8px\">FJD {quote.Total:N2}</td></tr></table><p><strong>Valid until:</strong> {quote.ValidUntil:dd MMM yyyy}<br><strong>Job site:</strong> {HtmlEncoder.Default.Encode(quote.JobSite ?? "Not specified")}<br><strong>Purchase order:</strong> {HtmlEncoder.Default.Encode(quote.PurchaseOrderNumber ?? "Not supplied")}</p><p>To accept or discuss this quotation, reply to the branch team or contact {HtmlEncoder.Default.Encode(branch.Phone ?? "the issuing branch")}.</p>";
        var email = emailQueue.Queue(db, customer.Email, $"Quotation {quote.QuoteNumber} · {division?.Name ?? "Carpenters Fiji"}", EmailTemplate.Branded($"Quotation {quote.QuoteNumber}", content, division?.Name ?? "Carpenters Fiji"), $"Quotation {quote.QuoteNumber}. Total FJD {quote.Total:N2}. Valid until {quote.ValidUntil:dd MMM yyyy}. Contact {branch.Name} to accept or discuss.", "Quotation");
        var now = DateTimeOffset.UtcNow; quote.Status = QuoteStatus.Sent; quote.LastEmailedTo = customer.Email.Trim(); quote.LastEmailedAt = now; quote.LastEmailId = email.Id; quote.UpdatedAt = now;
        AuditWriter.Record(db, scope, "Quotation queued for email", nameof(SalesQuote), quote.Id, $"{quote.QuoteNumber} queued to {quote.LastEmailedTo}.", quote.BranchId);
        await db.SaveChangesAsync(token); return Ok(new { quote.Id, quote.QuoteNumber, quote.Status, quote.LastEmailedTo, quote.LastEmailedAt, emailId = email.Id, emailStatus = email.Status });
    }

    [HttpPost("quotes/{id:guid}/convert")]
    public async Task<ActionResult> ConvertQuote(Guid id, ConvertQuoteRequest request, CancellationToken token)
    {
        var quote = await db.SalesQuotes.FirstOrDefaultAsync(x => x.Id == id, token); if (quote is null) return NotFound();
        var scope = await ScopeFor(quote.BranchId); if (scope is null || (!scope.IsAdministrator && quote.DivisionId != scope.DivisionId)) return Forbid();
        if (quote.Status != QuoteStatus.Accepted || quote.ConvertedBookingId.HasValue) return Validation("status", "Only an accepted quote that has not already been converted can create a booking.");
        if (request.EndAt <= request.StartAt || request.StartAt < DateTimeOffset.UtcNow.AddHours(-1) || request.DailyRate < 0 || request.DepositRequired < 0) return Validation("booking", "Enter a valid future hire period, rate and refundable bond.");
        var asset = await db.Assets.FirstOrDefaultAsync(x => x.Id == request.AssetId && x.BranchId == quote.BranchId && x.DivisionId == quote.DivisionId, token);
        if (asset is null || !BookingPolicy.IsOperational(asset)) return Validation("assetId", "Select an operational asset from the quote division and branch.");
        var conflict = await db.BookingItems.AnyAsync(x => x.AssetId == asset.Id && x.StartAt < request.EndAt && x.EndAt > request.StartAt && (x.Booking!.Status == BookingStatus.Confirmed || x.Booking.Status == BookingStatus.ConvertedToRental || x.Booking.Status == BookingStatus.Draft && x.Booking.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-30)), token);
        if (conflict) return Conflict(new { message = "The selected asset is held or booked for part of this period." });
        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == quote.CustomerId && x.IsActive && !x.IsBlocked, token); if (customer is null) return Validation("customer", "The quoted customer is no longer eligible to rent.");
        var days = Math.Max(1, (decimal)Math.Ceiling((request.EndAt - request.StartAt).TotalDays)); var rentalBase = request.DailyRate * days;
        if (rentalBase > quote.Subtotal) return Validation("dailyRate", "The base asset charge cannot exceed the accepted quote subtotal. Adjust the daily rate or quote lines.");
        var taxableBeforeTax = Math.Max(0, quote.Subtotal - quote.Discount); var taxRate = taxableBeforeTax == 0 ? 0 : decimal.Round(quote.Tax * 100m / taxableBeforeTax, 2);
        var quoteLines = JsonSerializer.Deserialize<IReadOnlyList<QuoteLine>>(quote.LineItemsJson ?? "[]") ?? [];
        var componentLines = quoteLines.Where(x => x.Category != ChargeCategory.BaseHire).ToList();
        var booking = new Booking { BookingNumber = $"BK-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..7].ToUpperInvariant()}", CustomerId = quote.CustomerId, BranchId = quote.BranchId, Status = BookingStatus.Confirmed, Notes = $"Created from accepted quote {quote.QuoteNumber}. Job site: {quote.JobSite ?? "Not recorded"}. PO: {quote.PurchaseOrderNumber ?? "Not recorded"}. {Clean(request.Note)}", DiscountAmount = quote.Discount, TaxRate = taxRate, DepositRequired = request.DepositRequired, AdditionalCharges = componentLines.Sum(x => x.Quantity * x.Rate), AdditionalChargesDescription = componentLines.Count == 0 ? null : string.Join(", ", componentLines.Select(x => x.Description)), ApprovedByUserId = scope.UserId, ApprovedAt = DateTimeOffset.UtcNow, Items = [new BookingItem { AssetId = asset.Id, StartAt = request.StartAt, EndAt = request.EndAt, DailyRate = request.DailyRate }], Charges = componentLines.Select(x => new BookingCharge { AssetId = asset.Id, ChargeDefinitionId = x.ChargeDefinitionId, Description = x.Description.Trim(), Category = x.Category, Unit = x.Unit, Quantity = x.Quantity, UnitRate = x.Rate, UnitCost = x.CostRate, IsTaxable = true }).ToList() };
        db.Bookings.Add(booking); quote.Status = QuoteStatus.Converted; quote.ConvertedBookingId = booking.Id; quote.UpdatedAt = DateTimeOffset.UtcNow;
        AuditWriter.Record(db, scope, "Quote converted to booking", "SalesQuote", quote.Id, $"{quote.QuoteNumber} created {booking.BookingNumber}.", quote.BranchId);
        await db.SaveChangesAsync(token); return Ok(new { quote.Id, quote.QuoteNumber, quote.Status, bookingId = booking.Id, booking.BookingNumber });
    }

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
    private IQueryable<Booking> BookingScope(StaffDataScope scope) => db.Bookings.AsNoTracking().Where(b => scope.IsAdministrator || scope.BranchIds.Contains(b.BranchId) && b.Items.Any() && b.Items.All(i => i.Asset != null && i.Asset.DivisionId.HasValue && scope.DivisionIds.Contains(i.Asset.DivisionId.Value)));
    private IQueryable<T> Scoped<T>(IQueryable<T> query, StaffDataScope scope, System.Linq.Expressions.Expression<Func<T, Guid>> branch) => scope.IsAdministrator ? query : query.Where(System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(System.Linq.Expressions.Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), new[] { typeof(Guid) }, System.Linq.Expressions.Expression.Constant(scope.BranchIds.ToArray()), branch.Body), branch.Parameters));
    private IQueryable<T> ScopedNullable<T>(IQueryable<T> query, StaffDataScope scope, System.Linq.Expressions.Expression<Func<T, Guid?>> branch) => scope.IsAdministrator ? query : query.Where(BuildEqual(branch, (Guid?)scope.BranchId));
    private IQueryable<AssetTransfer> TransferScope(StaffDataScope scope) => scope.IsAdministrator ? db.AssetTransfers.AsNoTracking() : db.AssetTransfers.AsNoTracking().Where(x => (scope.BranchIds.Contains(x.FromBranchId) || scope.BranchIds.Contains(x.ToBranchId)) && db.Assets.Any(a => a.Id == x.AssetId && a.DivisionId.HasValue && scope.DivisionIds.Contains(a.DivisionId.Value)));
    private IQueryable<SalesQuote> QuoteScope(StaffDataScope scope) => scope.IsAdministrator
        ? db.SalesQuotes.AsNoTracking()
        : db.SalesQuotes.AsNoTracking().Where(x => scope.BranchIds.Contains(x.BranchId) && x.DivisionId.HasValue && scope.DivisionIds.Contains(x.DivisionId.Value));
    private IQueryable<ApprovalRequest> ApprovalScope(StaffDataScope scope) => scope.IsAdministrator
        ? db.ApprovalRequests.AsNoTracking()
        : db.ApprovalRequests.AsNoTracking().Where(x => scope.BranchIds.Contains(x.BranchId) &&
            (x.EntityType != nameof(SalesQuote) || db.SalesQuotes.Any(q => q.Id == x.EntityId && q.DivisionId.HasValue && scope.DivisionIds.Contains(q.DivisionId.Value))) &&
            (x.EntityType != nameof(Booking) || db.Bookings.Any(b => b.Id == x.EntityId && b.Items.Any(i => i.Asset != null && i.Asset.DivisionId.HasValue && scope.DivisionIds.Contains(i.Asset.DivisionId.Value)))));
    private static System.Linq.Expressions.Expression<Func<T, bool>> BuildEqual<T, TValue>(System.Linq.Expressions.Expression<Func<T, TValue>> selector, TValue value) { var body = System.Linq.Expressions.Expression.Equal(selector.Body, System.Linq.Expressions.Expression.Constant(value, typeof(TValue))); return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, selector.Parameters); }
    private async Task SaveAudit(StaffDataScope scope, string action, Guid id, string summary, Guid? branch, CancellationToken token) { AuditWriter.Record(db, scope, action, "CorporateOperation", id, summary, branch); await db.SaveChangesAsync(token); }
    private static string Number(string prefix) => $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private BadRequestObjectResult Validation(string key, string message) => BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { [key] = [message] }));
}

public sealed record QuoteLine(string Description, decimal Quantity, decimal Rate, ChargeUnit Unit = ChargeUnit.Unit, decimal CostRate = 0, ChargeCategory Category = ChargeCategory.Other, Guid? ChargeDefinitionId = null);
public sealed record QuoteRequest(Guid CustomerId, Guid BranchId, Guid? DivisionId, DateTimeOffset ValidUntil, decimal Discount, decimal TaxRate, string? JobSite, string? PurchaseOrderNumber, IReadOnlyList<QuoteLine> Lines);
public sealed record ReviseQuoteRequest(DateTimeOffset ValidUntil, decimal Discount, decimal TaxRate, [Required] string Reason, IReadOnlyList<QuoteLine> Lines);
public sealed record CorporateAccountRequest(Guid CustomerId, string LegalName, string? TaxIdentificationNumber, decimal CreditLimit, int PaymentTermsDays, bool PurchaseOrderRequired, bool CreditHold, object? BillingContact, IReadOnlyList<object>? AuthorizedContacts, IReadOnlyList<object>? JobSites, object? ContractPricing);
public sealed record DispatchRequest(Guid BookingId, Guid BranchId, DispatchType Type, DateTimeOffset ScheduledAt, string? Address, string? AssignedDriver, string? TransportVehicle, decimal DeliveryCharge, Guid? DeliveryZoneId = null, decimal DistanceKilometres = 0, decimal InternalTransportCost = 0, decimal FailedDeliveryCharge = 0);
public sealed record TransferRequest(Guid AssetId, Guid FromBranchId, Guid ToBranchId, string Reason, decimal TransferCost);
public sealed record PricingRequest(string Name, Guid? BranchId, Guid? CustomerId, string? AssetType, RatePeriod Period, decimal Rate, decimal IncludedUsage, decimal ExcessUsageRate, int MinimumDuration, DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo, Guid? DivisionId = null, Guid? ChargeDefinitionId = null, string? CustomerType = null, decimal WeekendMultiplier = 1, decimal HolidayMultiplier = 1, decimal OvertimeMultiplier = 1);
public sealed record ApprovalRequestDto(Guid BranchId, ApprovalType Type, string EntityType, Guid EntityId, decimal Amount, string Reason);
public sealed record DecisionRequest(bool Approved, string? Note);
public sealed record PartRequest(string PartNumber, string Name, Guid BranchId, string? Supplier, decimal UnitCost, int QuantityOnHand, int ReorderLevel);
public sealed record PurchaseOrderLine(string PartNumber, string Description, int Quantity, decimal UnitCost);
public sealed record PurchaseOrderRequest(Guid BranchId, string Supplier, decimal Total, IReadOnlyList<PurchaseOrderLine> Lines);
public sealed record CaseRequest(Guid CustomerId, Guid BranchId, CaseType Type, CasePriority Priority, string Subject, string Description, Guid? AssignedUserId, DateTimeOffset DueAt);
public sealed record TaskRequest(Guid? BranchId, TaskCategory Category, TaskPriority Priority, string Title, string? Description, DateTimeOffset DueAt, Guid? AssignedUserId);
public sealed record TelematicsRequest(Guid AssetId, string Provider, DateTimeOffset RecordedAt, decimal? Latitude, decimal? Longitude, decimal? Odometer, decimal? EngineHours, decimal? FuelPercent, IReadOnlyList<string>? FaultCodes, bool UnauthorizedMovement);
public sealed record QuoteStatusRequest(QuoteStatus Status, string? Note);
public sealed record ConvertQuoteRequest(Guid AssetId, DateTimeOffset StartAt, DateTimeOffset EndAt, decimal DailyRate, decimal DepositRequired, string? Note);
public sealed record DispatchStatusRequest(DispatchStatus Status, string? SignatureName, string? Notes, IReadOnlyList<string>? EvidenceDataUrls);
public sealed record TransferStatusRequest(TransferStatus Status, decimal? MeterReading, string? ConditionNotes, string? DamageNotes, IReadOnlyList<string>? EvidenceDataUrls);
public sealed record DocumentRequest(string EntityType, Guid EntityId, Guid? BranchId, string Type, string FileName, string StoragePath, DateOnly? ExpiresOn, string? ContentHash);
