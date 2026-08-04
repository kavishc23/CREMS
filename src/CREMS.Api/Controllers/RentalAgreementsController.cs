using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/rental-agreements")]
[Authorize(Policy = SystemPolicies.ManageRentals)]
public sealed class RentalAgreementsController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    private const string TermsVersion = "CREMS-RA-2026.1-DRAFT";

    [HttpGet("{bookingId:guid}")]
    public async Task<ActionResult> Get(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await LoadBooking(bookingId, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasBranchAccess(booking.BranchId)) return Forbid();
        var agreement = await db.RentalAgreements.AsNoTracking().FirstOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        return Ok(agreement is null ? BuildDraft(booking) : BuildApproved(agreement, booking));
    }

    [HttpPost("{bookingId:guid}/approve")]
    public async Task<ActionResult> Approve(Guid bookingId, ApproveRentalAgreementRequest request, CancellationToken cancellationToken)
    {
        var booking = await LoadBooking(bookingId, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasBranchAccess(booking.BranchId)) return Forbid();
        if (booking.Status != BookingStatus.Confirmed)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["status"] = ["The booking must be confirmed before its rental agreement is signed."] }));
        if (!request.CustomerAcceptedTerms || !request.AgentApproved || string.IsNullOrWhiteSpace(request.CustomerSignatureName))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["signature"] = ["Customer acceptance, customer signature and agent approval are required."] }));
        if (await db.RentalAgreements.AnyAsync(item => item.BookingId == bookingId, cancellationToken))
            return Conflict(new { message = "This booking already has an approved rental agreement." });

        var draft = BuildAgreementData(booking);
        var now = DateTimeOffset.UtcNow;
        var agreement = new RentalAgreement
        {
            AgreementNumber = $"RA-{now:yyyy}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            BookingId = booking.Id, BranchId = booking.BranchId, TermsVersion = TermsVersion,
            TermsJson = JsonSerializer.Serialize(draft.Terms), CustomerSnapshotJson = JsonSerializer.Serialize(draft.Customer),
            AssetSnapshotJson = JsonSerializer.Serialize(draft.Asset), PricingSnapshotJson = JsonSerializer.Serialize(draft.Pricing),
            CustomerSignatureName = request.CustomerSignatureName.Trim(), CustomerSignedAt = now,
            ApprovedByUserId = scope.UserId, ApprovedByName = scope.UserName, ApprovedAt = now,
        };
        db.RentalAgreements.Add(agreement);
        AuditWriter.Record(db, scope, "Rental agreement approved", "RentalAgreement", agreement.Id,
            $"{agreement.AgreementNumber} was signed for {booking.BookingNumber}.", booking.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(BuildApproved(agreement, booking));
    }

    private async Task<Booking?> LoadBooking(Guid id, CancellationToken cancellationToken) => await db.Bookings
        .Include(item => item.Customer).Include(item => item.Branch).Include(item => item.Items).ThenInclude(item => item.Asset)
        .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    private static object BuildDraft(Booking booking)
    {
        var data = BuildAgreementData(booking);
        return new { approved = false, agreementNumber = (string?)null, termsVersion = TermsVersion,
            data.Customer, data.Asset, data.Rental, data.Pricing, data.Terms,
            customerSignatureName = (string?)null, customerSignedAt = (DateTimeOffset?)null,
            approvedByName = (string?)null, approvedAt = (DateTimeOffset?)null };
    }

    private static object BuildApproved(RentalAgreement agreement, Booking booking)
    {
        var current = BuildAgreementData(booking);
        return new { approved = true, agreement.AgreementNumber, agreement.TermsVersion,
            Customer = JsonSerializer.Deserialize<object>(agreement.CustomerSnapshotJson),
            Asset = JsonSerializer.Deserialize<object>(agreement.AssetSnapshotJson), current.Rental,
            Pricing = JsonSerializer.Deserialize<object>(agreement.PricingSnapshotJson),
            Terms = JsonSerializer.Deserialize<object>(agreement.TermsJson),
            agreement.CustomerSignatureName, agreement.CustomerSignedAt, agreement.ApprovedByName, agreement.ApprovedAt };
    }

    private static AgreementData BuildAgreementData(Booking booking)
    {
        var item = booking.Items.First();
        var days = Math.Max(1, (decimal)Math.Ceiling((item.EndAt - item.StartAt).TotalDays));
        var subtotal = days * item.DailyRate;
        var taxable = Math.Max(0, subtotal - booking.DiscountAmount + booking.AdditionalCharges);
        var tax = taxable * booking.TaxRate / 100m;
        return new AgreementData(
            new { booking.CustomerId, booking.Customer!.CustomerNumber, booking.Customer.Name, Type = booking.Customer.Type.ToString(), booking.Customer.Email, booking.Customer.Phone, booking.Customer.Address, booking.Customer.IdentificationNumber },
            new { item.AssetId, item.Asset!.AssetNumber, item.Asset.Name, Type = item.Asset.Type.ToString(), item.Asset.RegistrationNumber, item.Asset.SerialNumber },
            new { booking.Id, booking.BookingNumber, booking.BranchId, BranchName = booking.Branch!.Name, BranchAddress = booking.Branch.Address, BranchPhone = booking.Branch.Phone, item.StartAt, item.EndAt, Days = days },
            new { item.DailyRate, Subtotal = subtotal, booking.DiscountAmount, booking.AdditionalCharges, booking.AdditionalChargesDescription, booking.TaxRate, TaxAmount = tax, Total = taxable + tax, booking.DepositRequired },
            DefaultTerms());
    }

    private static IReadOnlyList<AgreementTerm> DefaultTerms() =>
    [
        new("1. Agreement and rental period", "This agreement, the booking summary, pricing schedule and signed inspection records form the entire rental record. The customer must return the asset to the stated branch by the agreed date and time unless Carpenters Rentals approves an extension in writing."),
        new("2. Charges, deposit and payment", "The customer must pay the stated rental charges, taxes, deposit and any properly assessed additional charges. Extra time, excess kilometres or hours, refuelling, cleaning, recovery, fines, tolls, loss and damage may be charged where applicable and disclosed."),
        new("3. Inspection and acceptance", "Before collection, the customer and rental officer must inspect the asset and record fuel, mileage or operating hours and existing damage. Signing confirms receipt in the recorded condition and suitability for the stated purpose, subject to defects that could not reasonably be identified."),
        new("4. Authorized drivers and operators", "Only persons approved by Carpenters Rentals who hold the legally required licence, permit, certification and competence may drive or operate the asset. The customer must not subhire, lend or transfer possession without written approval."),
        new("5. Safe and permitted use", "The asset must be used carefully, lawfully, within rated capacity and manufacturer instructions, only for the disclosed purpose and within any agreed geographic limits. Racing, towing without approval, overloading, unlawful use, use while impaired and reckless or unsafe operation are prohibited."),
        new("6. Care, fuel and routine checks", "The customer must secure the asset, use the correct fuel or power source and perform reasonable daily checks. No alteration or repair may be made without approval, except urgent action reasonably required to prevent injury or further damage."),
        new("7. Breakdown, accident, theft and damage", "The customer must stop using an unsafe asset and promptly contact Carpenters Rentals. Accidents, theft, loss and damage must be reported immediately, with police or other official reports obtained where required. The customer must not admit liability or arrange repairs without authorization."),
        new("8. Insurance and liability", "Any insurance, damage waiver, excess, exclusions and customer liability are only those expressly stated in the booking or an attached schedule. The customer remains responsible for excluded, unauthorized, reckless, unlawful or negligent use to the extent permitted by applicable law."),
        new("9. Return condition and inspection", "The asset must be returned with all supplied accessories and documents in substantially the same condition, fair wear and tear excepted, and at the agreed fuel or charge level. A return inspection may identify cleaning, fuel, late-return, missing-item or repair charges."),
        new("10. Fines and enforcement costs", "The customer is responsible for traffic, parking, toll, regulatory and operating penalties arising during the rental and reasonable administration costs connected with identifying the responsible driver or operator, where lawful."),
        new("11. Default and recovery", "Carpenters Rentals may terminate the rental and recover the asset where payment is overdue, information is materially false, use is unauthorized or unsafe, or the agreement is otherwise materially breached, subject to applicable law."),
        new("12. Privacy and records", "Customer information, identification, signatures, inspections and transaction records may be collected and used to administer the rental, protect assets, meet legal obligations and handle claims. Access must be limited to authorized purposes."),
        new("13. Governing law and disputes", "This agreement is governed by the laws of Fiji. The parties should first attempt to resolve concerns through the Carpenters Rentals branch. Nothing in this agreement excludes rights or remedies that cannot lawfully be excluded."),
        new("14. Acknowledgement", "The customer confirms that the details are accurate, the pricing and material terms were explained or made available before signing, questions could be asked, and a copy of the approved agreement will be provided."),
    ];

    private sealed record AgreementData(object Customer, object Asset, object Rental, object Pricing, IReadOnlyList<AgreementTerm> Terms);
    private sealed record AgreementTerm(string Title, string Content);
}

public sealed record ApproveRentalAgreementRequest(string CustomerSignatureName, bool CustomerAcceptedTerms, bool AgentApproved);
