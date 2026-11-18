using System.ComponentModel.DataAnnotations;
using System.Text.Encodings.Web;
using System.Text.Json;
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
[Route("api/rentals/{bookingId:guid}/incident-settlement")]
[Authorize(Policy = SystemPolicies.ManageRentals)]
public sealed class IncidentSettlementController(ApplicationDbContext db, CurrentStaffScope staffScope, IEmailQueue emailQueue,
    IAuthorizationService? authorization = null) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get(Guid bookingId, CancellationToken token)
    {
        var booking = await Load(bookingId, token);
        if (booking?.IncidentClosedAt is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasBranchAccess(booking.BranchId)) return Forbid();
        var task = await FollowUp(bookingId, token);
        var ownerId = task?.AssignedUserId ?? booking.Branch?.BranchManagerUserId;
        var canApprove = authorization is not null && (await authorization.AuthorizeAsync(User, SystemPolicies.ManageFinance)).Succeeded;
        if (!canApprove && (task?.AssignedUserId != scope.UserId || !scope.HasDivisionAccess(booking.Items.FirstOrDefault()?.Asset?.DivisionId)))
            return Forbid();
        var owner = await db.Users.Where(x => x.Id == ownerId && x.IsActive).Select(x => x.FullName).FirstOrDefaultAsync(token);
        var incidents = await db.RentalIncidents.AsNoTracking().Where(x => x.BookingId == bookingId)
            .Select(x => new { x.IncidentNumber, x.Status, x.Description, x.PoliceReference, x.InsuranceClaimNumber, x.EvidenceJson, x.InspectionJson, x.Outcome }).ToListAsync(token);
        var state = Read(await db.IncidentSettlements.AsNoTracking().SingleOrDefaultAsync(x => x.BookingId == bookingId, token));
        var assignees = canApprove && state.ClosedAt is null ? await EligibleAssignees(booking).ToListAsync(token) : [];
        var accountChangedSinceIssue = canApprove && state.ClosedAt is null && await AccountChangedSinceIssue(booking, state, token);
        return Ok(new
        {
            state, owner, ownerId, assignees, canApprove, accountChangedSinceIssue, followUpAt = task?.DueAt,
            lines = booking.Invoice is { } account ? InvoiceDocumentLines.Read(account) : RentalFinancials.Lines(booking),
            incidents, booking.Customer?.Email, booking.TaxRate, taxInclusive = booking.Invoice?.TaxInclusive == true,
            paid = booking.Invoice?.AmountPaid ?? RentalFinancials.Paid(booking), total = booking.Invoice?.Total ?? RentalFinancials.Total(booking),
            balance = booking.Invoice?.BalanceDue ?? Math.Max(0, RentalFinancials.Total(booking) - RentalFinancials.Paid(booking)),
            booking.BondAmountHeld, booking.BondDeductionAmount, booking.BondSettledAt, booking.BondRefundAmount,
            invoice = booking.Invoice is { } invoice ? new { invoice.Id, invoice.InvoiceNumber, invoice.Status } : null
        });
    }

    [HttpPut]
    [Authorize(Policy = SystemPolicies.ManageFinance)]
    public async Task<ActionResult> Save(Guid bookingId, IncidentSettlementRequest request, CancellationToken token)
    {
        await using var transaction = await MaintenanceRules.BeginAsync(db, token);
        var booking = await Load(bookingId, token);
        if (booking?.IncidentClosedAt is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasBranchAccess(booking.BranchId)) return Forbid();
        var settlement = await db.IncidentSettlements.SingleOrDefaultAsync(x => x.BookingId == bookingId, token);
        var previous = Read(settlement);
        if (previous.Revision != request.Revision || previous.ClosedAt.HasValue)
            return Conflict(new { message = "This settlement has changed or was closed. Reopen it to see the current record." });

        var task = await FollowUp(bookingId, token);
        var ownerId = request.AssignedUserId ?? (request.TakeOwnership ? scope.UserId : task?.AssignedUserId ?? booking.Branch?.BranchManagerUserId);
        if (request.AssignedUserId.HasValue && !await EligibleAssignees(booking).AnyAsync(x => x.Id == ownerId, token))
            return Error("Select an active rental officer or manager with access to this hire.");
        if (!await db.Users.AnyAsync(x => x.Id == ownerId && x.IsActive, token))
            return Error("Assign yourself as case owner before saving.");
        var now = DateTimeOffset.UtcNow;
        var today = MaintenanceRules.LocalDate(now);
        var basis = request.Basis?.Trim();
        if (IncidentEvidence.Validate(request.Attachments.Select(x => x.DataUrl).ToArray()) is { } evidenceError)
            return Error(evidenceError);
        if (previous.Attachments.Any(x => !request.Attachments.Contains(x)))
            return Error("Previously saved evidence must stay with the case.");
        var costs = request.Costs.Select(x => x with
        {
            Description = x.Description.Trim(), Responsibility = x.Responsibility.Trim(), Evidence = x.Evidence.Trim()
        }).ToArray();
        if (costs.Select(x => x.Id).Distinct().Count() != costs.Length ||
            costs.Any(x => x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Description) || x.Amount != RentalFinancials.Money(x.Amount) ||
                x.Decision is not ("Pending" or "Customer" or "Other") ||
                x.Decision != "Pending" && (x.Evidence.Length == 0 || x.Responsibility.Length == 0) ||
                x.Decision == "Customer" && x.Amount <= 0))
            return Error("Enter a description for each cost, and record the responsible party and evidence for each decision.");
        if (previous.Costs.Where(x => x.Billed).Any(x => !costs.Contains(x)) ||
            costs.Where(x => x.Billed).Any(x => !previous.Costs.Contains(x)))
            return Error("Issued charges must be adjusted through invoice credits.");
        if (previous.Costs.Any(x => !costs.Any(cost => cost.Id == x.Id)))
            return Error("Record a decision for previously saved costs, including costs that will not be charged.");
        if (request.FollowUpDate == default)
            return Error("Enter a follow-up date for the case.");
        if (request.SettleBond && !request.IssueInvoice)
            return Error("Issue the settlement with the bond decision.");
        if (request.CloseCase && (request.IssueInvoice || previous.IssuedAt is null || booking.Invoice is null ||
            booking.Invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Voided || booking.Invoice.BalanceDue > 0 ||
            costs.Any(x => x.Decision == "Pending" || x.Decision == "Customer" && !x.Billed) ||
            booking.BondAmountHeld > 0 && booking.BondSettledAt is null || request.Disputed || string.IsNullOrWhiteSpace(request.Outcome)))
            return Error("Record the outcome, resolve disputes, costs and the bond, and clear the invoice balance before closing the case.");
        if (request.Disputed && string.IsNullOrWhiteSpace(request.Outcome))
            return Error("Record what the customer disputes and the next follow-up action.");

        var invoice = booking.Invoice;
        DateOnly? paymentDueDate = null;
        if (request.IssueInvoice)
        {
            if (basis is null || basis.Length < 10)
                return Error("Record the agreement clause or agreed settlement.");
            if (invoice?.Status is InvoiceStatus.Draft or InvoiceStatus.Voided)
                return Error("Resolve the existing draft or voided invoice before issuing the incident settlement.");
            if (previous.IssuedAt.HasValue && request.InvoiceCredit != 0)
                return Error("Use Add credit note to adjust an issued settlement.");

            var approved = costs.Where(x => x.Decision == "Customer" && !x.Billed).ToArray();
            var addedSubtotal = approved.Sum(x => x.Amount);
            var taxInclusive = invoice?.TaxInclusive == true;
            var addedTax = RentalFinancials.Money(approved.Where(x => x.IsTaxable).Sum(x =>
                x.Amount * booking.TaxRate / (taxInclusive ? 100 + booking.TaxRate : 100)));
            var paid = invoice?.AmountPaid ?? RentalFinancials.Paid(booking);
            var total = (invoice?.Total ?? RentalFinancials.Total(booking)) + addedSubtotal + (taxInclusive ? 0 : addedTax);
            var credit = request.InvoiceCredit;
            var bond = request.BondCredit;
            var refund = booking.BondAmountHeld - bond;
            var refundReference = request.BondRefundReference?.Trim();
            if (total < paid)
                return Error("The hire has an overpayment. Resolve it before issuing the settlement.");
            if (credit < 0 || credit > total - paid || credit != RentalFinancials.Money(credit) ||
                credit > 0 && string.IsNullOrWhiteSpace(request.InvoiceCreditReason))
                return Error("Record the authority for any agreed credit and keep it within the unpaid invoice amount.");
            if (request.SettleBond)
            {
                if (booking.BondSettledAt.HasValue || booking.BondAmountHeld <= 0 || bond < 0 || bond > booking.BondAmountHeld ||
                    bond > total - paid - credit || bond != RentalFinancials.Money(bond))
                    return Error("Apply no more than the held bond or the unpaid invoice amount.");
                if (bond > 0 && string.IsNullOrWhiteSpace(request.BondReason))
                    return Error("Record why the bond credit is authorized.");
                if (refund > 0 && (string.IsNullOrEmpty(refundReference) || !Enum.IsDefined(request.BondRefundMethod) ||
                    request.BondRefundMethod == PaymentMethod.PurchaseOrder))
                    return Error("Record the method and transaction reference for the bond refund already paid.");
                if (refund > 0 && await db.RentalPayments.AnyAsync(x => x.ReceiptNumber == refundReference, token))
                    return Conflict(new { message = "That refund reference is already recorded." });
            }

            var balanceAfterSettlement = total - paid - credit - (request.SettleBond ? bond : 0);
            if (balanceAfterSettlement > 0)
            {
                if (request.DueDate is null || request.DueDate < today)
                    return Error("Enter a payment due date for the outstanding balance.");
                paymentDueDate = request.DueDate;
            }
            var dueDateChanged = balanceAfterSettlement > 0 && previous.DueDate != paymentDueDate;
            if (previous.IssuedAt.HasValue && !dueDateChanged && approved.Length == 0 && credit == 0 &&
                !request.SettleBond && !await AccountChangedSinceIssue(booking, previous, token))
                return Conflict(new { message = "This settlement has already been issued. There are no account changes to issue." });

            if (invoice is null)
            {
                invoice = new RentalInvoice
                {
                    BookingId = bookingId, InvoiceNumber = $"INV-{now:yyyy}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                    LineItemsJson = "[]", Subtotal = RentalFinancials.Net(booking), TaxAmount = HirePricing.Tax(booking),
                    Lines = RentalFinancials.Lines(booking).Select(x => new InvoiceLine
                    {
                        Description = x.Description, Quantity = x.Quantity, UnitPrice = x.Rate,
                        TaxRate = booking.TaxRate, IsTaxable = x.IsTaxable
                    }).ToList()
                };
                db.RentalInvoices.Add(invoice);
            }
            else if (approved.Length > 0 && invoice.Lines.Count == 0)
            {
                // Materialize legacy invoice lines before appending, so the old lines stay visible.
                var legacyLines = InvoiceDocumentLines.Read(invoice);
                if (legacyLines.Count == 0 && invoice.Subtotal > 0)
                    legacyLines = [new FinancialLine("Previously invoiced charges", 1, invoice.Subtotal, "Unit", true)];
                foreach (var line in legacyLines)
                    db.InvoiceLines.Add(new InvoiceLine { Invoice = invoice, Description = line.Description,
                        Quantity = line.Quantity, UnitPrice = line.Rate, TaxRate = booking.TaxRate, IsTaxable = line.IsTaxable });
            }
            booking.AdditionalCharges = Math.Max(booking.AdditionalCharges, RentalFinancials.Services(booking)) + addedSubtotal;
            foreach (var cost in approved)
            {
                db.BookingCharges.Add(new BookingCharge { Booking = booking, Description = cost.Description,
                    Quantity = 1, Unit = ChargeUnit.Fixed, UnitRate = cost.Amount, UnitCost = cost.Amount, Category = ChargeCategory.Other, IsTaxable = cost.IsTaxable });
                db.InvoiceLines.Add(new InvoiceLine { Invoice = invoice, Description = cost.Description,
                    Quantity = 1, UnitPrice = cost.Amount, TaxRate = booking.TaxRate, IsTaxable = cost.IsTaxable });
            }
            costs = costs.Select(x => x.Decision == "Customer" ? x with { Billed = true } : x).ToArray();
            if (request.SettleBond)
            {
                booking.BondDeductionAmount = bond;
                booking.BondDeductionReason = request.BondReason?.Trim();
                booking.BondRefundAmount = refund;
                booking.BondSettledAt = now;
                booking.BondStatus = refund == 0 ? BondStatus.Retained : bond == 0 ? BondStatus.Refunded : BondStatus.PartiallyRefunded;
                if (refund > 0)
                    db.RentalPayments.Add(new RentalPayment { BookingId = bookingId, Type = PaymentType.BondRefund,
                        Method = request.BondRefundMethod, Amount = refund, ReceiptNumber = refundReference!,
                        Note = "Incident settlement bond refund", RecordedByUserId = scope.UserId, RecordedByName = scope.UserName });
                paid += bond;
            }
            invoice.Subtotal += addedSubtotal;
            invoice.TaxAmount += addedTax;
            invoice.Total = total;
            invoice.AmountPaid = paid;
            invoice.BalanceDue = total - paid;
            invoice.Status = paid == total ? InvoiceStatus.Paid : paid > 0 ? InvoiceStatus.PartiallyPaid : InvoiceStatus.Issued;
            if (credit > 0)
            {
                var previousCredits = await db.CreditNotes.Where(x => x.InvoiceId == invoice.Id && x.Status == CreditNoteStatus.Applied)
                    .SumAsync(x => (decimal?)x.Total, token) ?? 0;
                db.CreditNotes.Add(RentalFinancials.ApplyCredit(invoice, credit, previousCredits, request.InvoiceCreditReason!, scope.UserId));
            }
            if (invoice.Lines.Count > 0)
                invoice.LineItemsJson = JsonSerializer.Serialize(invoice.Lines.Select(x =>
                    new { x.Description, x.Quantity, x.UnitPrice, x.TaxRate, x.IsTaxable }));
            if (!string.IsNullOrWhiteSpace(booking.Customer?.Email))
                QueueStatement(booking, invoice, paymentDueDate);
        }

        if (task is null)
        {
            task = new ManagementTask { BranchId = booking.BranchId, Category = TaskCategory.CustomerCase,
                Priority = TaskPriority.High, Title = $"Incident follow-up for {booking.BookingNumber}",
                SourceEntityType = nameof(Booking), SourceEntityId = bookingId,
                Description = "Review incident liability, invoice, bond and outstanding payments." };
            db.ManagementTasks.Add(task);
        }
        task.AssignedUserId = ownerId;
        task.DueAt = new DateTimeOffset(request.FollowUpDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(12));
        task.IsCompleted = request.CloseCase;
        booking.UpdatedAt = now;
        var state = new IncidentSettlementState(previous.Revision + 1, costs, request.Attachments, basis,
            request.IssueInvoice ? paymentDueDate : previous.IssuedAt.HasValue ? previous.DueDate : request.DueDate,
            request.Outcome?.Trim(), request.Disputed, request.IssueInvoice ? now : previous.IssuedAt,
            request.IssueInvoice ? scope.UserName : previous.IssuedBy, request.CloseCase ? now : null, previous.FollowUpNotes,
            request.IssueInvoice ? IssuedAccount(booking, invoice!) : previous.IssuedAccount);
        var details = JsonSerializer.Serialize(state);
        if (settlement is null)
        {
            settlement = new IncidentSettlement { BookingId = bookingId, DetailsJson = details };
            db.IncidentSettlements.Add(settlement);
        }
        settlement.DetailsJson = details;
        settlement.ClosedAt = state.ClosedAt;
        if (request.CloseCase)
            foreach (var incident in await db.RentalIncidents.Where(i => i.BookingId == bookingId && i.Status != IncidentStatus.Closed).ToListAsync(token))
            {
                incident.Status = IncidentStatus.Closed;
                incident.CustomerResolution = "The branch has completed the incident settlement. View your final invoice and payment position, or contact the branch for clarification.";
                await SharedRentalIncidents.SyncCase(db, booking, incident, token);
            }
        if (request.IssueInvoice) settlement.PaymentDueDate = state.DueDate;
        AuditWriter.Record(db, scope, request.CloseCase ? "Incident settlement closed" : request.IssueInvoice ?
            "Incident settlement issued" : "Incident settlement reviewed", nameof(Booking), bookingId,
            $"Incident settlement for {booking.BookingNumber}", booking.BranchId, null,
            $"Owner: {ownerId}; Follow-up: {request.FollowUpDate:yyyy-MM-dd}; Invoice: {invoice?.InvoiceNumber}; " +
            $"Balance: {invoice?.BalanceDue:0.00}; Bond credit: {booking.BondDeductionAmount:0.00}; " +
            $"Invoice credit: {(request.IssueInvoice ? request.InvoiceCredit : 0):0.00}; Credit reason: {request.InvoiceCreditReason}; " +
            $"Payment basis: {basis}; Disputed: {state.Disputed}; Outcome: {state.Outcome}; Costs: {JsonSerializer.Serialize(costs)}");
        await db.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        return Ok(new { state.Revision, invoice?.InvoiceNumber, emailQueued = request.IssueInvoice && !string.IsNullOrWhiteSpace(booking.Customer?.Email) });
    }

    [HttpPatch("follow-up")]
    public async Task<ActionResult> UpdateFollowUp(Guid bookingId, IncidentFollowUpRequest request, CancellationToken token)
    {
        await using var transaction = await MaintenanceRules.BeginAsync(db, token);
        var booking = await Load(bookingId, token);
        if (booking?.IncidentClosedAt is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        var task = await FollowUp(bookingId, token);
        if (scope is null || task?.AssignedUserId != scope.UserId ||
            !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        var settlement = await db.IncidentSettlements.SingleOrDefaultAsync(x => x.BookingId == bookingId, token);
        var previous = Read(settlement);
        if (previous.Revision != request.Revision || previous.ClosedAt.HasValue || task.IsCompleted)
            return Conflict(new { message = "This case has changed or closed. Reopen it to see the current record." });
        if (request.FollowUpDate == default || string.IsNullOrWhiteSpace(request.Notes))
            return Error("Enter a follow-up date and describe what you found or what happens next.");
        var notes = request.Notes.Trim();
        var details = JsonSerializer.Serialize(previous with { Revision = previous.Revision + 1, FollowUpNotes = notes });
        if (settlement is null)
            db.IncidentSettlements.Add(new IncidentSettlement { BookingId = bookingId, DetailsJson = details });
        else
            settlement.DetailsJson = details;
        task.DueAt = new DateTimeOffset(request.FollowUpDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(12));
        AuditWriter.Record(db, scope, "Incident follow-up updated", nameof(Booking), bookingId,
            $"Incident follow-up for {booking.BookingNumber}", booking.BranchId, previous.FollowUpNotes, notes);
        await db.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        return NoContent();
    }

    private IQueryable<IncidentAssignee> EligibleAssignees(Booking booking)
    {
        var divisionId = booking.Items.FirstOrDefault()?.Asset?.DivisionId;
        var now = DateTimeOffset.UtcNow;
        var grants = db.UserAccessScopes.Where(x => x.IsActive && x.EffectiveFrom <= now && (x.ExpiresAt == null || x.ExpiresAt > now));
        return (from user in db.Users
                join membership in db.UserRoles on user.Id equals membership.UserId
                join role in db.Roles on membership.RoleId equals role.Id
                where user.IsActive && (role.Name == SystemRoles.RentalOfficer || role.Name == SystemRoles.BranchManager ||
                    role.Name == SystemRoles.Administrator || role.Name == SystemRoles.SuperAdministrator)
                where role.Name == SystemRoles.Administrator || role.Name == SystemRoles.SuperAdministrator ||
                    grants.Any(x => x.UserId == user.Id && x.Type == AccessScopeType.GroupWide) ||
                    ((user.BranchId == booking.BranchId || grants.Any(x => x.UserId == user.Id && x.BranchId == booking.BranchId)) &&
                     divisionId != null && (user.DivisionId == divisionId || grants.Any(x => x.UserId == user.Id && x.DivisionId == divisionId)))
                select new IncidentAssignee { Id = user.Id, Name = user.FullName, Role = role.Name! })
            .OrderBy(x => x.Role == SystemRoles.RentalOfficer ? 0 : 1).ThenBy(x => x.Name);
    }

    private void QueueStatement(Booking booking, RentalInvoice invoice, DateOnly? dueDate)
    {
        var lines = InvoiceDocumentLines.Read(invoice);
        var adjustment = invoice.Total - invoice.Subtotal - (invoice.TaxInclusive ? 0 : invoice.TaxAmount);
        var text = $"Incident settlement for booking {booking.BookingNumber}\n" +
            string.Join("\n", lines.Select(x => $"{x.Description}: FJD {x.Amount:0.00}")) +
            $"\nVAT{(invoice.TaxInclusive ? " included" : "")}: FJD {invoice.TaxAmount:0.00}" +
            (adjustment == 0 ? "" : $"\nInvoice adjustments and credits: FJD {adjustment:0.00}") +
            $"\nInvoice total: FJD {invoice.Total:0.00}" +
            $"\nHire payments applied: FJD {invoice.AmountPaid - (booking.BondSettledAt.HasValue ? booking.BondDeductionAmount : 0):0.00}" +
            $"\nBond credit: FJD {(booking.BondSettledAt.HasValue ? booking.BondDeductionAmount : 0):0.00}" +
            $"\nBalance due: FJD {invoice.BalanceDue:0.00}" +
            (dueDate.HasValue ? $"\nPayment due: {dueDate:dd MMM yyyy}" : "\nNo payment is due.") +
            "\nContact the issuing branch if you have questions about this settlement.";
        var subject = $"Incident settlement {invoice.InvoiceNumber}";
        emailQueue.Queue(db, booking.Customer!.Email!, subject,
            EmailTemplate.Branded(subject, $"<p>{HtmlEncoder.Default.Encode(text).Replace("\n", "<br>")}</p>"),
            text, "IncidentSettlement");
    }

    private async Task<bool> AccountChangedSinceIssue(Booking booking, IncidentSettlementState state, CancellationToken token)
    {
        if (state.IssuedAt is not { } issuedAt || booking.Invoice is not { } invoice) return false;
        if (state.IssuedAccount is { } account) return account != IssuedAccount(booking, invoice);

        // Older settlements lack an account snapshot. Use separately recorded changes since issue.
        return booking.Payments.Any(payment => payment.CreatedAt > issuedAt && payment.Status == PaymentStatus.Recorded &&
            payment.Type is PaymentType.RentalCharge or PaymentType.AdditionalCharge) ||
            booking.BondSettledAt > issuedAt ||
            await db.AuditEvents.AnyAsync(entry => entry.EntityId == booking.Id && entry.EntityType == nameof(Booking) &&
                entry.Action == "Incident settlement credit issued" && entry.CreatedAt > issuedAt, token);
    }

    private static IncidentIssuedAccount IssuedAccount(Booking booking, RentalInvoice invoice) =>
        new(invoice.Id, invoice.Subtotal, invoice.TaxAmount, invoice.Total, invoice.AmountPaid, invoice.BalanceDue,
            booking.BondSettledAt.HasValue ? booking.BondDeductionAmount : 0, booking.BondRefundAmount);

    private Task<Booking?> Load(Guid id, CancellationToken token) => db.Bookings.Include(x => x.Branch)
        .Include(x => x.Customer).Include(x => x.Items).ThenInclude(x => x.Asset)
        .Include(x => x.Payments).Include(x => x.Charges).Include(x => x.Invoice).ThenInclude(x => x!.Lines)
        .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, token);

    private Task<ManagementTask?> FollowUp(Guid id, CancellationToken token) => db.ManagementTasks
        .FirstOrDefaultAsync(x => x.SourceEntityType == nameof(Booking) && x.SourceEntityId == id &&
            x.Category == TaskCategory.CustomerCase && x.Title.StartsWith("Incident follow-up for "), token);

    private static IncidentSettlementState Read(IncidentSettlement? settlement) => settlement?.DetailsJson is { } json
        ? JsonSerializer.Deserialize<IncidentSettlementState>(json)! : new(0, [], [], null, null, null, false, null, null, null);

    private BadRequestObjectResult Error(string message) => BadRequest(new { message });
}

public sealed record IncidentCostDecision(Guid Id, [Required, StringLength(240)] string Description,
    [Range(0, 1000000)] decimal Amount, string Decision, [StringLength(150)] string Responsibility,
    [StringLength(1000)] string Evidence, bool IsTaxable, bool Billed);

public sealed record IncidentSettlementAttachment([Required, StringLength(255)] string Name, [Required] string DataUrl);

public sealed record IncidentSettlementState(int Revision, IReadOnlyList<IncidentCostDecision> Costs,
    IReadOnlyList<IncidentSettlementAttachment> Attachments,
    string? Basis, DateOnly? DueDate, string? Outcome, bool Disputed, DateTimeOffset? IssuedAt, string? IssuedBy, DateTimeOffset? ClosedAt,
    string? FollowUpNotes = null, IncidentIssuedAccount? IssuedAccount = null);

public sealed record IncidentIssuedAccount(Guid InvoiceId, decimal Subtotal, decimal Tax, decimal Total,
    decimal Paid, decimal Balance, decimal BondCredit, decimal BondRefund);

public sealed class IncidentAssignee
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Role { get; init; }
}
public sealed record IncidentFollowUpRequest(int Revision, DateOnly FollowUpDate, [Required, StringLength(1000)] string Notes);

public sealed record IncidentSettlementRequest(int Revision, bool TakeOwnership, DateOnly FollowUpDate,
    [Required, MaxLength(50)] IReadOnlyList<IncidentCostDecision> Costs,
    [Required, MaxLength(5)] IReadOnlyList<IncidentSettlementAttachment> Attachments, [StringLength(1000)] string? Basis,
    DateOnly? DueDate, bool IssueInvoice, bool SettleBond, decimal BondCredit, [StringLength(500)] string? BondReason,
    PaymentMethod BondRefundMethod, [StringLength(60)] string? BondRefundReference, bool CloseCase, bool Disputed,
    [StringLength(1000)] string? Outcome, decimal InvoiceCredit = 0, [StringLength(500)] string? InvoiceCreditReason = null,
    Guid? AssignedUserId = null);
