using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
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
[Route("api/bookings")]
[Authorize(Policy = SystemPolicies.ManageRentals)]
public sealed class BookingsController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet("work-queue")]
    public async Task<ActionResult<BookingWorkQueueResponse>> GetWorkQueue(
        [FromQuery] string queue = "NewRequests", [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && !scope.BranchId.HasValue)) return Forbid();
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 10, 100);

        var baseQuery = db.Bookings.AsNoTracking();
        if (!scope.IsAdministrator)
            baseQuery = baseQuery.Where(x => x.BranchId == scope.BranchId && x.Items.Any(i => i.Asset!.DivisionId == scope.DivisionId));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            baseQuery = baseQuery.Where(x => x.BookingNumber.Contains(term) || x.Customer!.Name.Contains(term) ||
                (x.Customer.Email != null && x.Customer.Email.Contains(term)) ||
                x.Items.Any(i => i.Asset!.Name.Contains(term) || i.Asset.AssetNumber.Contains(term)));
        }

        var draftWithApproval = db.ApprovalRequests.AsNoTracking()
            .Where(x => x.EntityType == nameof(Booking) && x.Status == ApprovalStatus.Pending).Select(x => x.EntityId);
        var quotedBookings = db.SalesQuotes.AsNoTracking().Where(x => x.ConvertedBookingId != null).Select(x => x.ConvertedBookingId!.Value);
        var quotationRequired = baseQuery.Where(x => x.Status == BookingStatus.Draft && !draftWithApproval.Contains(x.Id) &&
            (quotedBookings.Contains(x.Id) || x.Customer!.Type == CREMS.Api.Domain.Customers.CustomerType.Business || x.Items.Any(i => i.Asset!.Type == AssetType.Equipment)));

        IQueryable<Booking> selected = queue switch
        {
            "QuotationRequired" => quotationRequired,
            "AwaitingApproval" => baseQuery.Where(x => x.Status == BookingStatus.Draft && draftWithApproval.Contains(x.Id)),
            "Confirmed" => baseQuery.Where(x => x.Status == BookingStatus.Confirmed),
            "Closed" => baseQuery.Where(x => x.Status == BookingStatus.Cancelled || x.Status == BookingStatus.Expired),
            _ => baseQuery.Where(x => x.Status == BookingStatus.Draft && !draftWithApproval.Contains(x.Id) &&
                !quotedBookings.Contains(x.Id) && x.Customer!.Type == CREMS.Api.Domain.Customers.CustomerType.Individual && !x.Items.Any(i => i.Asset!.Type == AssetType.Equipment)),
        };

        var counts = new BookingQueueCounts(
            await baseQuery.CountAsync(x => x.Status == BookingStatus.Draft && !draftWithApproval.Contains(x.Id) && !quotedBookings.Contains(x.Id) && x.Customer!.Type == CREMS.Api.Domain.Customers.CustomerType.Individual && !x.Items.Any(i => i.Asset!.Type == AssetType.Equipment), cancellationToken),
            await quotationRequired.CountAsync(cancellationToken),
            await baseQuery.CountAsync(x => x.Status == BookingStatus.Draft && draftWithApproval.Contains(x.Id), cancellationToken),
            await baseQuery.CountAsync(x => x.Status == BookingStatus.Confirmed, cancellationToken),
            await baseQuery.CountAsync(x => x.Status == BookingStatus.Cancelled || x.Status == BookingStatus.Expired, cancellationToken));
        var total = await selected.CountAsync(cancellationToken);
        var rows = await selected.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new BookingWorkQueueRow(
                x.Id, x.BookingNumber, x.CreatedAt, x.Status.ToString(), x.CustomerId, x.Customer!.Name,
                x.Customer.Email, x.Customer.Phone, x.Customer.IsBlocked, x.Customer.Type.ToString(),
                x.BranchId, x.Branch!.Name,
                x.Items.Select(i => i.Asset!.Division != null ? i.Asset.Division.Name : "Unassigned division").FirstOrDefault() ?? "Unassigned division",
                x.Items.Select(i => i.Asset!.ServiceOffering != null ? i.Asset.ServiceOffering.Name : i.Asset.Category).FirstOrDefault() ?? "General hire",
                x.Items.Select(i => i.Asset!.AssetNumber).FirstOrDefault(), x.Items.Select(i => i.Asset!.Name).FirstOrDefault(),
                x.Items.Select(i => i.Asset!.Category).FirstOrDefault(), x.Items.Select(i => (DateTimeOffset?)i.StartAt).FirstOrDefault(),
                x.Items.Select(i => (DateTimeOffset?)i.EndAt).FirstOrDefault(),
                // Keep the duration calculation inside SQL. DateTimeOffset subtraction followed by
                // TotalDays/Math.Ceiling cannot be translated by the SQL Server EF provider.
                x.Items.Sum(i => i.DailyRate * (decimal)(
                    EF.Functions.DateDiffMinute(i.StartAt, i.EndAt) <= 1440
                        ? 1
                        : (EF.Functions.DateDiffMinute(i.StartAt, i.EndAt) + 1439) / 1440))
                    - x.DiscountAmount + x.AdditionalCharges,
                x.DepositRequired, x.ApprovedAt, x.RentalAgreement != null,
                x.Customer.IsBlocked ? "Customer account is blocked" : x.Items.Count == 0 ? "Asset information is missing" :
                    x.Items.Any(i => !i.Asset!.IsActive || i.Asset.Status == AssetStatus.Maintenance || i.Asset.Status == AssetStatus.OutOfService) ? "Asset is not available" : null))
            .ToListAsync(cancellationToken);
        return Ok(new BookingWorkQueueResponse(rows, counts, page, pageSize, total));
    }

    [HttpGet("{id:guid}/workspace")]
    public async Task<ActionResult> GetWorkspace(Guid id, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.AsNoTracking().Include(x => x.Customer).Include(x => x.Branch)
            .Include(x => x.Items).ThenInclude(x => x.Asset).ThenInclude(x => x!.Division)
            .Include(x => x.Items).ThenInclude(x => x.Asset).ThenInclude(x => x!.ServiceOffering)
            .Include(x => x.Charges).Include(x => x.Inspections).Include(x => x.Payments).Include(x => x.Invoice)
            .Include(x => x.RentalAgreement).ThenInclude(x => x!.Addendums).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        var quote = await db.SalesQuotes.AsNoTracking().FirstOrDefaultAsync(x => x.ConvertedBookingId == id, cancellationToken);
        var quoteId = quote?.Id;
        var approvals = await db.ApprovalRequests.AsNoTracking().Where(x => (x.EntityType == nameof(Booking) && x.EntityId == id) || (quoteId != null && x.EntityType == nameof(SalesQuote) && x.EntityId == quoteId))
            .OrderByDescending(x => x.CreatedAt).Select(x => new { x.RequestNumber, status = x.Status.ToString(), x.Amount, x.Reason, x.CurrentStage, x.TotalStages, x.DecisionNote, x.CreatedAt, x.DecidedAt }).ToListAsync(cancellationToken);
        var activity = await db.AuditEvents.AsNoTracking().Where(x => x.EntityType == nameof(Booking) && x.EntityId == id)
            .OrderByDescending(x => x.OccurredAt).Take(50).Select(x => new { x.Id, x.Action, x.Summary, x.UserName, x.OccurredAt }).ToListAsync(cancellationToken);
        var documents = await db.DocumentRecords.AsNoTracking().Where(x => x.EntityType == nameof(Booking) && x.EntityId == id)
            .OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.FileName, x.Type, x.StoragePath, x.CreatedAt }).ToListAsync(cancellationToken);
        var customerRequests = await db.CustomerCases.AsNoTracking().Where(x => x.CustomerId == booking.CustomerId && x.Subject.Contains(booking.BookingNumber))
            .OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.CaseNumber, type = x.Type.ToString(), priority = x.Priority.ToString(), status = x.Status.ToString(), x.Subject, x.Description, x.CreatedAt, x.DueAt }).ToListAsync(cancellationToken);
        var item = booking.Items.FirstOrDefault();
        var days = item is null ? 0 : Math.Max(1, (decimal)Math.Ceiling((item.EndAt - item.StartAt).TotalDays));
        var hire = item is null ? 0 : days * item.DailyRate; var subtotal = Math.Max(0, hire - booking.DiscountAmount + booking.AdditionalCharges);
        var paid = booking.Payments.Where(x => x.Status == PaymentStatus.Recorded).Sum(x => x.Amount);
        var approvalContext = BuildApprovalContext(booking, subtotal * (1 + booking.TaxRate / 100m));
        var approvalRule = await ApprovalWorkflowService.MatchBookingAsync(db, approvalContext, cancellationToken);
        return Ok(new {
            booking.Id, booking.BookingNumber, status = booking.Status.ToString(), booking.CreatedAt, booking.Notes,
            customer = new { booking.CustomerId, booking.Customer!.CustomerNumber, booking.Customer.Name, type = booking.Customer.Type.ToString(), booking.Customer.Email, booking.Customer.Phone, booking.Customer.Address, booking.Customer.IdentificationNumber, booking.Customer.IsBlocked },
            branch = new { booking.BranchId, booking.Branch!.Name, booking.Branch.Address, booking.Branch.Phone },
            item = item is null ? null : new { item.Id, item.AssetId, item.Asset!.AssetNumber, item.Asset.Name, item.Asset.Category, assetStatus = item.Asset.Status.ToString(), personnelRequirement = item.Asset.PersonnelRequirement.ToString(), item.Asset.DivisionId, division = item.Asset.Division?.Name, item.Asset.ServiceOfferingId, service = item.Asset.ServiceOffering?.Name, item.StartAt, item.EndAt, item.DailyRate, item.Asset.CurrentMeterReading, item.Asset.MeterUnit },
            pricing = new { duration = days, hire, booking.DiscountAmount, booking.AdditionalCharges, booking.AdditionalChargesDescription, booking.TaxRate, tax = subtotal * booking.TaxRate / 100m, total = subtotal * (1 + booking.TaxRate / 100m), booking.DepositRequired, paid, balance = Math.Max(0, subtotal * (1 + booking.TaxRate / 100m) - paid), internalCost = booking.Charges.Sum(x => x.Quantity * x.UnitCost), charges = booking.Charges.Select(x => new { x.Description, category = x.Category.ToString(), unit = x.Unit.ToString(), x.Quantity, x.UnitRate, x.UnitCost, x.IsTaxable }) },
            readiness = new { confirmed = booking.Status == BookingStatus.Confirmed, assetAllocated = item != null, customerEligible = !booking.Customer.IsBlocked && booking.Customer.IsActive, identificationVerified = booking.Inspections.Any(x => x.Type == InspectionType.Handover && x.IdentificationVerified), licenceVerified = booking.Inspections.Any(x => x.Type == InspectionType.Handover && x.DriverLicenceVerified), paymentSatisfied = paid >= booking.DepositRequired, preHireInspectionComplete = booking.Inspections.Any(x => x.Type == InspectionType.Handover), agreementSigned = booking.RentalAgreement != null },
            agreement = booking.RentalAgreement is null ? null : new { booking.RentalAgreement.AgreementNumber, status = booking.RentalAgreement.Status.ToString(), booking.RentalAgreement.CustomerSignedAt, booking.RentalAgreement.ApprovedByName, booking.RentalAgreement.LastEmailedAt, addendumCount = booking.RentalAgreement.Addendums.Count },
            quotation = quote is null ? null : new { quote.Id, quote.QuoteNumber, status = quote.Status.ToString(), quote.ValidUntil, quote.Subtotal, quote.Discount, quote.Tax, quote.Total, quote.Version, quote.LineItemsJson, quote.LastEmailedTo, quote.LastEmailedAt },
            invoice = booking.Invoice is null ? null : new { booking.Invoice.InvoiceNumber, status = booking.Invoice.Status.ToString(), booking.Invoice.Total, booking.Invoice.AmountPaid, booking.Invoice.BalanceDue },
            approvalRule = approvalRule is null ? null : new { approvalRule.WorkflowName, approvalRule.Reason, stages = approvalRule.Stages.Select(x => new { x.Sequence, x.Name, x.AssignedRole, x.EscalateAfterHours }) }, approvals, customerRequests, documents, activity
        });
    }

    [HttpPost("{id:guid}/quotation")]
    public async Task<ActionResult> PrepareQuotation(Guid id, PrepareBookingQuotationRequest request, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(x => x.Customer).Include(x => x.Items).ThenInclude(x => x.Asset).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        if (booking.Status != BookingStatus.Draft) return BadRequest(new { message = "Only a new request can be quoted." });
        if (request.ValidUntil <= DateTimeOffset.UtcNow || request.Lines.Count == 0 || request.Lines.Any(x => string.IsNullOrWhiteSpace(x.Description) || x.Quantity <= 0 || x.Rate < 0 || x.CostRate < 0) || request.Discount < 0 || request.TaxRate is < 0 or > 100)
            return BadRequest(new { message = "Enter valid quotation lines, pricing, tax and a future expiry date." });
        var existing = await db.SalesQuotes.FirstOrDefaultAsync(x => x.ConvertedBookingId == id, cancellationToken);
        var totals = QuotePolicy.Calculate(request.Lines.Select(x => (x.Quantity, x.Rate)), request.Discount, request.TaxRate);
        if (request.Discount > totals.Subtotal) return BadRequest(new { message = "Discount cannot exceed the quotation subtotal." });
        var quote = existing ?? new SalesQuote { QuoteNumber = $"QT-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}", CustomerId = booking.CustomerId, BranchId = booking.BranchId, DivisionId = booking.Items.FirstOrDefault()?.Asset?.DivisionId, AssignedUserId = scope.UserId, ConvertedBookingId = booking.Id, ValidUntil = request.ValidUntil };
        if (existing is not null)
        {
            db.QuoteRevisions.Add(new CREMS.Api.Domain.Operations.QuoteRevision { SalesQuoteId = quote.Id, Version = quote.Version, SnapshotJson = System.Text.Json.JsonSerializer.Serialize(new { quote.ValidUntil, quote.Subtotal, quote.Discount, quote.Tax, quote.Total, quote.LineItemsJson }), ChangeReason = string.IsNullOrWhiteSpace(request.RevisionReason) ? "Quotation updated" : request.RevisionReason.Trim(), ChangedByUserId = scope.UserId });
            quote.Version += 1;
        }
        else db.SalesQuotes.Add(quote);
        quote.ValidUntil = request.ValidUntil; quote.Discount = request.Discount; quote.Subtotal = totals.Subtotal; quote.Tax = totals.Tax; quote.Total = totals.Total; quote.LineItemsJson = System.Text.Json.JsonSerializer.Serialize(request.Lines); quote.Status = QuoteStatus.Draft; quote.UpdatedAt = DateTimeOffset.UtcNow;
        booking.DiscountAmount = request.Discount; booking.TaxRate = request.TaxRate; booking.AdditionalCharges = Math.Max(0, totals.Subtotal - booking.Items.Sum(x => x.DailyRate * Math.Max(1, (decimal)Math.Ceiling((x.EndAt - x.StartAt).TotalDays)))); booking.UpdatedAt = DateTimeOffset.UtcNow;
        if (request.Discount > 0 && !await db.ApprovalRequests.AnyAsync(x => x.EntityType == nameof(SalesQuote) && x.EntityId == quote.Id && x.Status == ApprovalStatus.Pending, cancellationToken))
        {
            var approval = new ApprovalRequest { RequestNumber = $"APR-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}", BranchId = booking.BranchId, Type = ApprovalType.Discount, EntityType = nameof(SalesQuote), EntityId = quote.Id, Amount = request.Discount, Reason = $"Discount approval for {quote.QuoteNumber}", RequestedByUserId = scope.UserId };
            await ApprovalWorkflowService.ConfigureAsync(db, approval, quote.DivisionId, cancellationToken); db.ApprovalRequests.Add(approval);
        }
        AuditWriter.Record(db, scope, existing is null ? "Quotation prepared" : "Quotation revised", nameof(Booking), booking.Id, $"{quote.QuoteNumber} version {quote.Version} prepared for {booking.BookingNumber}.", booking.BranchId);
        await db.SaveChangesAsync(cancellationToken); return Ok(new { quote.Id, quote.QuoteNumber, quote.Version, quote.Subtotal, quote.Discount, quote.Tax, quote.Total, status = quote.Status.ToString() });
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BookingResponse>>> GetAll(
        [FromQuery] BookingStatus? status,
        CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && !scope.BranchId.HasValue)) return Forbid();
        var query = db.Bookings.AsNoTracking();
        if (!scope.IsAdministrator) query = query.Where(booking => booking.BranchId == scope.BranchId && booking.Items.Any(item => item.Asset!.DivisionId == scope.DivisionId));
        if (status.HasValue) query = query.Where(booking => booking.Status == status);

        var bookings = await query
            .OrderByDescending(booking => booking.CreatedAt)
            .Select(booking => new BookingResponse(
                booking.Id,
                booking.BookingNumber,
                booking.Status,
                booking.CreatedAt,
                booking.Notes,
                booking.CustomerId,
                booking.Customer!.Name,
                booking.Customer.Email,
                booking.Customer.Phone,
                booking.Customer.IsBlocked,
                booking.BranchId,
                booking.Branch!.Name,
                booking.DiscountAmount,
                booking.TaxRate,
                booking.DepositRequired,
                booking.AdditionalCharges,
                booking.AdditionalChargesDescription,
                booking.ApprovedAt,
                db.RentalAgreements.Any(agreement => agreement.BookingId == booking.Id),
                booking.Items.Select(item => new BookingItemResponse(
                    item.Id,
                    item.AssetId,
                    item.Asset!.AssetNumber,
                    item.Asset.Name,
                    item.StartAt,
                    item.EndAt,
                    item.DailyRate)).ToList(),
                booking.Charges.Select(x => new BookingChargeResponse(x.Id, x.ChargeDefinitionId, x.Description, x.Category, x.Unit, x.Quantity, x.UnitRate, x.UnitCost, x.IsTaxable)).ToList()))
            .ToListAsync(cancellationToken);
        return Ok(bookings);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BookingResponse>> Update(
        Guid id,
        UpdateBookingRequest request,
        CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(item => item.Customer).Include(item => item.Branch)
            .Include(item => item.Items).ThenInclude(item => item.Asset).Include(item => item.Charges)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        if (booking.Status is BookingStatus.ConvertedToRental or BookingStatus.Completed)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["status"] = ["Active or completed rentals cannot be reassigned."] }));

        var asset = await db.Assets.FirstOrDefaultAsync(item => item.Id == request.AssetId &&
            item.BranchId == request.BranchId && item.IsActive, cancellationToken);
        var customer = await db.Customers.FirstOrDefaultAsync(item => item.Id == request.CustomerId && item.IsActive, cancellationToken);
        var branch = await db.Branches.FirstOrDefaultAsync(item => item.Id == request.BranchId && item.IsActive, cancellationToken);
        if (asset is null || customer is null || branch is null || request.EndAt <= request.StartAt)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["booking"] = ["Select a valid branch, customer, asset and rental period."] }));
        if (!scope.HasAssetAccess(request.BranchId, asset.DivisionId)) return Forbid();
        if (request.StartAt < DateTimeOffset.UtcNow.AddDays(-1) || request.DailyRate < 0 || request.DepositRequired < 0 || request.DiscountAmount < 0 || request.AdditionalCharges < 0 || request.TaxRate is < 0 or > 100)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["pricing"] = ["Dates and financial values must be valid and non-negative."] }));
        if (asset.Status is AssetStatus.Maintenance or AssetStatus.OutOfService or AssetStatus.Retired || !asset.IsActive)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["asset"] = ["This asset is not operationally available."] }));
        var editConflict = await db.BookingItems.AsNoTracking().AnyAsync(other => other.BookingId != booking.Id &&
            other.AssetId == asset.Id && other.StartAt < request.EndAt && other.EndAt > request.StartAt &&
            (other.Booking!.Status == BookingStatus.Confirmed || other.Booking.Status == BookingStatus.ConvertedToRental ||
             other.Booking.Status == BookingStatus.Draft && other.Booking.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-30)), cancellationToken);
        if (editConflict)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["availability"] = ["This asset is already held or booked for part of the selected period."] }));

        var previous = $"Branch: {booking.BranchId}; Customer: {booking.CustomerId}; Asset: {booking.Items.FirstOrDefault()?.AssetId}";
        booking.BranchId = branch.Id; booking.Branch = branch; booking.CustomerId = customer.Id; booking.Customer = customer;
        booking.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        booking.DiscountAmount = request.DiscountAmount; booking.TaxRate = request.TaxRate;
        booking.DepositRequired = request.DepositRequired; booking.AdditionalCharges = request.AdditionalCharges;
        booking.AdditionalChargesDescription = string.IsNullOrWhiteSpace(request.AdditionalChargesDescription) ? null : request.AdditionalChargesDescription.Trim();
        var item = booking.Items.FirstOrDefault();
        if (item is null) { item = new BookingItem(); booking.Items.Add(item); }
        item.AssetId = asset.Id; item.Asset = asset; item.StartAt = request.StartAt; item.EndAt = request.EndAt; item.DailyRate = request.DailyRate;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        AuditWriter.Record(db, scope, "Booking updated", "Booking", booking.Id,
            $"{booking.BookingNumber} details and pricing were updated.", booking.BranchId, previous,
            $"Branch: {booking.BranchId}; Customer: {booking.CustomerId}; Asset: {asset.Id}");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(booking));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<BookingResponse>> SetStatus(
        Guid id,
        SetBookingStatusRequest request,
        CancellationToken cancellationToken)
    {
        var booking = await db.Bookings
            .Include(item => item.Customer)
            .Include(item => item.Branch)
            .Include(item => item.Items).ThenInclude(item => item.Asset).ThenInclude(item => item!.ServiceOffering)
            .Include(item => item.Charges)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();

        if (!BookingPolicy.IsValidTransition(booking.Status, request.Status))
        {
            ModelState.AddModelError(nameof(request.Status),
                $"A {booking.Status} booking cannot be changed to {request.Status}.");
            return ValidationProblem(ModelState);
        }

        if (request.Status == BookingStatus.Confirmed)
        {
            var firstItem = booking.Items.FirstOrDefault();
            if (firstItem is not null)
            {
                var localStart = firstItem.StartAt.ToOffset(TimeSpan.FromHours(12)); var localEnd = firstItem.EndAt.ToOffset(TimeSpan.FromHours(12));
                if (!await IsBranchOpen(booking.BranchId, localStart, true, cancellationToken) || !await IsBranchOpen(booking.BranchId, localEnd, false, cancellationToken)) { ModelState.AddModelError("calendar", "Pickup or return is outside the branch operating calendar or after its cut-off time."); return ValidationProblem(ModelState); }
                if (firstItem.Asset?.ServiceOfferingId is Guid serviceId && await db.BranchDivisionServices.AnyAsync(x => x.BranchId == booking.BranchId && x.ServiceOfferingId == serviceId) && !await db.BranchDivisionServices.AnyAsync(x => x.BranchId == booking.BranchId && x.ServiceOfferingId == serviceId && x.IsActive && x.IsBookable, cancellationToken)) { ModelState.AddModelError("service", "This service is not currently bookable at the selected branch."); return ValidationProblem(ModelState); }
            }
            if (booking.Customer?.IsBlocked == true || booking.Customer?.IsActive == false)
            {
                ModelState.AddModelError(nameof(booking.CustomerId),
                    "This customer is not currently eligible to rent.");
                return ValidationProblem(ModelState);
            }
            if (booking.Items.Count == 0 || booking.Items.Any(x => x.Asset is null || !x.Asset.IsActive ||
                x.Asset.Status is AssetStatus.Maintenance or AssetStatus.OutOfService or AssetStatus.Retired))
            { ModelState.AddModelError(nameof(booking.Items), "Every asset must be active and operationally available before confirmation."); return ValidationProblem(ModelState); }

            var duration = booking.Items.Sum(x => Math.Max(1, (decimal)Math.Ceiling((x.EndAt - x.StartAt).TotalDays)) * x.DailyRate);
            var subtotal = Math.Max(0, duration - booking.DiscountAmount + booking.AdditionalCharges);
            var approvalMatch = await ApprovalWorkflowService.MatchBookingAsync(db, BuildApprovalContext(booking, subtotal * (1 + booking.TaxRate / 100m)), cancellationToken);
            if (approvalMatch is not null)
            {
                var existing = await db.ApprovalRequests.Include(x => x.StageDecisions)
                    .Where(x => x.EntityType == nameof(Booking) && x.EntityId == booking.Id && x.WorkflowId == approvalMatch.WorkflowId)
                    .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
                if (existing?.Status == ApprovalStatus.Pending)
                    return Accepted(new { outcome = "AwaitingApproval", approvalRequestId = existing.Id, existing.RequestNumber, approvalMatch.WorkflowName, approvalMatch.Reason });
                if (existing?.Status != ApprovalStatus.Approved)
                {
                    var approval = new ApprovalRequest {
                        RequestNumber = $"APR-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                        BranchId = booking.BranchId, Type = ApprovalType.Booking, EntityType = nameof(Booking), EntityId = booking.Id,
                        Amount = subtotal * (1 + booking.TaxRate / 100m), Reason = $"{booking.BookingNumber}: {approvalMatch.Reason}", RequestedByUserId = scope.UserId
                    };
                    ApprovalWorkflowService.ConfigureFromMatch(approval, approvalMatch);
                    db.ApprovalRequests.Add(approval);
                    AuditWriter.Record(db, scope, "Booking submitted for approval", nameof(Booking), booking.Id,
                        $"{booking.BookingNumber} routed through {approvalMatch.WorkflowName}.", booking.BranchId);
                    await db.SaveChangesAsync(cancellationToken);
                    return Accepted(new { outcome = "AwaitingApproval", approvalRequestId = approval.Id, approval.RequestNumber, approvalMatch.WorkflowName, approvalMatch.Reason });
                }
            }
            booking.ApprovedByUserId = scope.UserId;
            booking.ApprovedAt = DateTimeOffset.UtcNow;

            foreach (var item in booking.Items)
            {
                var conflict = await db.BookingItems.AsNoTracking().AnyAsync(other =>
                    other.BookingId != booking.Id &&
                    other.AssetId == item.AssetId &&
                    other.StartAt < item.EndAt && other.EndAt > item.StartAt &&
                    (other.Booking!.Status == BookingStatus.Confirmed || other.Booking.Status == BookingStatus.ConvertedToRental ||
                     other.Booking.Status == BookingStatus.Draft && other.Booking.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-30)),
                    cancellationToken);
                if (conflict)
                {
                    ModelState.AddModelError(nameof(booking.Items),
                        $"{item.Asset?.Name ?? "An asset"} has another confirmed booking for these dates.");
                    return ValidationProblem(ModelState);
                }
            }
        }

        booking.Status = request.Status;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        if (request.Status == BookingStatus.ConvertedToRental)
        {
            foreach (var item in booking.Items.Where(item => item.Asset is not null))
                item.Asset!.Status = AssetStatus.Rented;
        }
        else if (request.Status == BookingStatus.Completed)
        {
            foreach (var item in booking.Items.Where(item => item.Asset is not null))
                item.Asset!.Status = AssetStatus.Available;
        }
        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            var note = request.Note.Trim();
            booking.Notes = string.IsNullOrWhiteSpace(booking.Notes)
                ? note
                : $"{booking.Notes}\nStaff note: {note}";
        }

        AuditWriter.Record(db, scope, "Booking status changed", "Booking", booking.Id,
            $"{booking.BookingNumber} changed to {request.Status}.", booking.BranchId,
            null, request.Status.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(booking));
    }

    private async Task<bool> IsBranchOpen(Guid branchId, DateTimeOffset local, bool pickup, CancellationToken token)
    {
        var date = DateOnly.FromDateTime(local.Date); var time = TimeOnly.FromDateTime(local.DateTime); var exception = await db.BranchCalendarExceptions.AsNoTracking().FirstOrDefaultAsync(x => x.BranchId == branchId && x.Date == date, token);
        if (exception is not null) return !exception.IsClosed && (!exception.OpensAt.HasValue || time >= exception.OpensAt) && (!exception.ClosesAt.HasValue || time <= exception.ClosesAt);
        var period = await db.BranchOperatingPeriods.AsNoTracking().FirstOrDefaultAsync(x => x.BranchId == branchId && x.DayOfWeek == local.DayOfWeek, token); if (period is null) return true; if (period.IsClosed || time < period.OpensAt || time > period.ClosesAt) return false; var cutoff = pickup ? period.PickupCutoff : period.ReturnCutoff; return !cutoff.HasValue || time <= cutoff;
    }

    private static ApprovalWorkflowService.BookingApprovalContext BuildApprovalContext(Booking booking, decimal amount)
    {
        var equipment = booking.Items.Any(x => x.Asset?.Type == AssetType.Equipment);
        var personnel = booking.Items.Any(x => x.Asset?.ServiceOffering?.PersonnelRequirement != PersonnelRequirement.None) ||
            booking.Charges.Any(x => x.Category is ChargeCategory.Operator or ChargeCategory.Driver or ChargeCategory.Labour);
        var overtime = booking.Charges.Any(x => x.Description.Contains("overtime", StringComparison.OrdinalIgnoreCase)) ||
            (booking.AdditionalChargesDescription?.Contains("overtime", StringComparison.OrdinalIgnoreCase) ?? false);
        return new ApprovalWorkflowService.BookingApprovalContext(booking.BranchId, booking.Items.Select(x => x.Asset?.DivisionId).FirstOrDefault(), amount, equipment, personnel, overtime);
    }

    [HttpPut("{id:guid}/charges")]
    public async Task<ActionResult> ReplaceCharges(Guid id, IReadOnlyList<SaveBookingChargeRequest> request, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(x => x.Items).ThenInclude(x => x.Asset).Include(x => x.Charges).FirstOrDefaultAsync(x => x.Id == id, cancellationToken); if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User); if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        if (booking.Status is BookingStatus.Completed or BookingStatus.Cancelled) return BadRequest(new { message = "Charges on a completed or cancelled rental cannot be replaced." });
        if (request.Any(x => string.IsNullOrWhiteSpace(x.Description) || x.Quantity <= 0 || x.UnitRate < 0 || x.UnitCost < 0)) return BadRequest(new { message = "Every charge requires a description, positive quantity and non-negative rates." });
        var definitionIds = request.Where(x => x.ChargeDefinitionId.HasValue).Select(x => x.ChargeDefinitionId!.Value).Distinct().ToList();
        if (definitionIds.Count > 0 && await db.ChargeDefinitions.CountAsync(x => definitionIds.Contains(x.Id) && x.IsActive, cancellationToken) != definitionIds.Count) return BadRequest(new { message = "One or more selected charge definitions are unavailable." });
        db.BookingCharges.RemoveRange(booking.Charges); booking.Charges = request.Select(x => new BookingCharge { BookingId = booking.Id, AssetId = x.AssetId ?? booking.Items.FirstOrDefault()?.AssetId, ChargeDefinitionId = x.ChargeDefinitionId, Description = x.Description.Trim(), Category = x.Category, Unit = x.Unit, Quantity = x.Quantity, UnitRate = x.UnitRate, UnitCost = x.UnitCost, IsTaxable = x.IsTaxable }).ToList();
        booking.AdditionalCharges = booking.Charges.Sum(x => x.Quantity * x.UnitRate); booking.AdditionalChargesDescription = booking.Charges.Count == 0 ? null : string.Join(", ", booking.Charges.Select(x => x.Description)); booking.UpdatedAt = DateTimeOffset.UtcNow;
        AuditWriter.Record(db, scope, "Booking charges replaced", nameof(Booking), booking.Id, $"{booking.BookingNumber} now has {booking.Charges.Count} configurable charge components.", booking.BranchId); await db.SaveChangesAsync(cancellationToken);
        return Ok(booking.Charges.Select(x => new BookingChargeResponse(x.Id, x.ChargeDefinitionId, x.Description, x.Category, x.Unit, x.Quantity, x.UnitRate, x.UnitCost, x.IsTaxable)));
    }

    private static BookingResponse ToResponse(Booking booking) => new(
        booking.Id, booking.BookingNumber, booking.Status, booking.CreatedAt, booking.Notes,
        booking.CustomerId, booking.Customer?.Name ?? string.Empty,
        booking.Customer?.Email, booking.Customer?.Phone, booking.Customer?.IsBlocked ?? false,
        booking.BranchId, booking.Branch?.Name ?? string.Empty,
        booking.DiscountAmount, booking.TaxRate, booking.DepositRequired, booking.AdditionalCharges,
        booking.AdditionalChargesDescription, booking.ApprovedAt,
        booking.RentalAgreement is not null,
        booking.Items.Select(item => new BookingItemResponse(
            item.Id, item.AssetId, item.Asset?.AssetNumber ?? string.Empty,
            item.Asset?.Name ?? string.Empty, item.StartAt, item.EndAt, item.DailyRate)).ToList(),
        booking.Charges.Select(x => new BookingChargeResponse(x.Id, x.ChargeDefinitionId, x.Description, x.Category, x.Unit, x.Quantity, x.UnitRate, x.UnitCost, x.IsTaxable)).ToList());
}

public sealed record SetBookingStatusRequest(BookingStatus Status, string? Note);
public sealed record UpdateBookingRequest(Guid BranchId, Guid CustomerId, Guid AssetId,
    DateTimeOffset StartAt, DateTimeOffset EndAt, decimal DailyRate, string? Notes,
    decimal DiscountAmount, decimal TaxRate, decimal DepositRequired, decimal AdditionalCharges,
    string? AdditionalChargesDescription);
public sealed record BookingItemResponse(
    Guid Id, Guid AssetId, string AssetNumber, string AssetName,
    DateTimeOffset StartAt, DateTimeOffset EndAt, decimal DailyRate);
public sealed record SaveBookingChargeRequest(Guid? AssetId, Guid? ChargeDefinitionId, string Description, ChargeCategory Category, ChargeUnit Unit, decimal Quantity, decimal UnitRate, decimal UnitCost, bool IsTaxable);
public sealed record BookingChargeResponse(Guid Id, Guid? ChargeDefinitionId, string Description, ChargeCategory Category, ChargeUnit Unit, decimal Quantity, decimal UnitRate, decimal UnitCost, bool IsTaxable);
public sealed record BookingResponse(
    Guid Id, string BookingNumber, BookingStatus Status, DateTimeOffset CreatedAt, string? Notes,
    Guid CustomerId, string CustomerName, string? CustomerEmail, string? CustomerPhone,
    bool CustomerIsBlocked, Guid BranchId, string BranchName,
    decimal DiscountAmount, decimal TaxRate, decimal DepositRequired, decimal AdditionalCharges,
    string? AdditionalChargesDescription, DateTimeOffset? ApprovedAt,
    bool HasAgreement,
    IReadOnlyCollection<BookingItemResponse> Items,
    IReadOnlyCollection<BookingChargeResponse> Charges);
public sealed record BookingQueueCounts(int NewRequests, int QuotationRequired, int AwaitingApproval, int Confirmed, int Closed);
public sealed record BookingWorkQueueResponse(IReadOnlyList<BookingWorkQueueRow> Items, BookingQueueCounts Counts, int Page, int PageSize, int Total);
public sealed record BookingWorkQueueRow(Guid Id, string BookingNumber, DateTimeOffset CreatedAt, string Status,
    Guid CustomerId, string CustomerName, string? CustomerEmail, string? CustomerPhone, bool CustomerIsBlocked,
    string CustomerType, Guid BranchId, string BranchName, string DivisionName, string ServiceName,
    string? AssetNumber, string? AssetName, string? Category, DateTimeOffset? StartAt, DateTimeOffset? EndAt,
    decimal EstimatedValue, decimal DepositRequired, DateTimeOffset? ApprovedAt, bool HasAgreement, string? Warning);
public sealed record PrepareBookingQuotationLine(string Description, decimal Quantity, decimal Rate, decimal CostRate, ChargeUnit Unit = ChargeUnit.Unit, ChargeCategory Category = ChargeCategory.Other);
public sealed record PrepareBookingQuotationRequest(DateTimeOffset ValidUntil, decimal Discount, decimal TaxRate, string? RevisionReason, IReadOnlyList<PrepareBookingQuotationLine> Lines);
