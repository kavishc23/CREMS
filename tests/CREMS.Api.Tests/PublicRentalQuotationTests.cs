using System.Security.Claims;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class PublicRentalQuotationTests
{
    [Fact]
    public async Task Quote_only_service_creates_complete_quotation_request_atomically()
    {
        await using var fixture = await QuotationFixture.CreateAsync(new EmailQueue());
        var cancellationToken = TestContext.Current.CancellationToken;

        var result = await fixture.Controller.RequestBooking(fixture.ValidRequest(), cancellationToken);

        var response = Assert.IsType<PublicBookingResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.StartsWith("QUO-", response.Reference);
        Assert.Equal("Quotation", response.RequestType);

        var booking = await fixture.Db.Bookings.Include(x => x.Items).Include(x => x.Charges).SingleAsync(cancellationToken);
        var quote = await fixture.Db.SalesQuotes.SingleAsync(cancellationToken);
        Assert.Equal(booking.Id, quote.ConvertedBookingId);
        Assert.Equal(9432m, quote.Total);
        Assert.Contains("\"unit\":\"Day\"", quote.LineItemsJson);
        Assert.Equal(ChargeUnit.Day, QuoteLineSerialization.Deserialize(quote.LineItemsJson).First().Unit);
        Assert.Single(booking.Items);
        Assert.Single(booking.Charges);
        Assert.Equal(56m, booking.Charges.Single().Quantity);
        Assert.Equal(1, await fixture.Db.CustomerCases.CountAsync(cancellationToken));
        Assert.Equal(1, await fixture.Db.OutboundEmails.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Hourly_request_uses_entered_hours_in_saved_price()
    {
        await using var fixture = await QuotationFixture.CreateAsync(new EmailQueue());
        var token = TestContext.Current.CancellationToken;
        var asset = await fixture.Db.Assets.SingleAsync(token);
        asset.HireUnit = ChargeUnit.Hour;
        asset.DailyRate = 10m;
        await fixture.Db.SaveChangesAsync(token);

        var response = await fixture.Controller.RequestBooking(fixture.ValidRequest() with { HireQuantity = 8m }, token);
        Assert.IsType<OkObjectResult>(response.Result);
        var item = await fixture.Db.BookingItems.SingleAsync(token);
        Assert.Equal(8m, item.HireQuantity);
        Assert.Equal(80m, item.HireQuantity * item.DailyRate);
    }

    [Fact]
    public async Task Asset_pickup_choice_overrides_a_service_delivery_default()
    {
        await using var fixture = await QuotationFixture.CreateAsync(new EmailQueue());
        var token = TestContext.Current.CancellationToken;
        var asset = await fixture.Db.Assets.Include(x => x.ServiceOffering).SingleAsync(token);
        asset.RequiresDelivery = false;
        Assert.True(asset.ServiceOffering!.RequiresDelivery);
        await fixture.Db.SaveChangesAsync(token);

        var response = await fixture.Controller.RequestBooking(fixture.ValidRequest() with { Fulfilment = "Pickup", DeliveryAddress = null }, token);
        Assert.IsType<OkObjectResult>(response.Result);
    }

    [Fact]
    public async Task Booking_only_service_stays_a_booking_even_with_division_quote_default()
    {
        await using var fixture = await QuotationFixture.CreateAsync(new EmailQueue());
        var token = TestContext.Current.CancellationToken;
        var asset = await fixture.Db.Assets.Include(x => x.ServiceOffering).SingleAsync(token);
        asset.RequiresDelivery = false;
        asset.ServiceOffering!.RequiresQuote = false;
        asset.ServiceOffering.AllowsQuoteRequests = false;
        await fixture.Db.SaveChangesAsync(token);

        var response = await fixture.Controller.RequestBooking(fixture.ValidRequest() with
        { Fulfilment = "Pickup", DeliveryAddress = null, RequestQuotation = false }, token);
        var request = Assert.IsType<PublicBookingResponse>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal("Booking", request.RequestType);
        Assert.Empty(await fixture.Db.SalesQuotes.ToListAsync(token));
    }

    [Fact]
    public async Task Later_confirmation_failure_does_not_leave_partial_request()
    {
        await using var fixture = await QuotationFixture.CreateAsync(new ThrowingEmailQueue());
        var cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Controller.RequestBooking(fixture.ValidRequest(), cancellationToken));

        Assert.Equal(0, await fixture.Db.Bookings.CountAsync(cancellationToken));
        Assert.Equal(0, await fixture.Db.SalesQuotes.CountAsync(cancellationToken));
        Assert.Equal(0, await fixture.Db.CustomerCases.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Optional_delivery_still_requires_a_destination()
    {
        await using var fixture = await QuotationFixture.CreateAsync(new EmailQueue());
        var token = TestContext.Current.CancellationToken;
        var asset = await fixture.Db.Assets.SingleAsync(token);
        asset.RequiresDelivery = false;
        asset.ServiceOffering!.RequiresDelivery = false;
        await fixture.Db.SaveChangesAsync(token);

        var response = await fixture.Controller.RequestBooking(fixture.ValidRequest() with { DeliveryAddress = null }, token);

        Assert.IsType<ObjectResult>(response.Result);
        Assert.Empty(await fixture.Db.Bookings.ToListAsync(token));
    }

    [Fact]
    public async Task Required_daily_fee_uses_hire_days_even_when_client_sends_quantity_one()
    {
        await using var fixture = await QuotationFixture.CreateAsync(new EmailQueue());
        var token = TestContext.Current.CancellationToken;
        var asset = await fixture.Db.Assets.SingleAsync(token);
        var fee = new ChargeDefinition { DivisionId = asset.DivisionId!.Value, Code = "DAILY-FEE", Name = "Daily fee",
            Category = ChargeCategory.Other, Unit = ChargeUnit.Day, DefaultSellingRate = 10, IsRequired = true };
        fixture.Db.ChargeDefinitions.Add(fee);
        await fixture.Db.SaveChangesAsync(token);

        var response = await fixture.Controller.RequestBooking(fixture.ValidRequest() with
        { Extras = [new PublicBookingExtraRequest(fee.Id, 1)] }, token);

        Assert.IsType<OkObjectResult>(response.Result);
        var charge = await fixture.Db.BookingCharges.SingleAsync(x => x.ChargeDefinitionId == fee.Id, token);
        Assert.Equal(6, charge.Quantity);
    }

    [Fact]
    public async Task Business_identity_comes_from_the_account_not_a_spoofed_request()
    {
        await using var fixture = await QuotationFixture.CreateAsync(new EmailQueue());
        var token = TestContext.Current.CancellationToken;
        var customer = await fixture.Db.Customers.SingleAsync(token);
        customer.Type = CustomerType.Business;
        customer.Name = "North Coast Builders Ltd";
        await fixture.Db.SaveChangesAsync(token);

        var response = await fixture.Controller.RequestBooking(fixture.ValidRequest() with
        { CustomerType = CustomerType.Individual, CompanyName = null }, token);

        Assert.IsType<OkObjectResult>(response.Result);
        var booking = await fixture.Db.Bookings.SingleAsync(token);
        Assert.Contains("North Coast Builders Ltd", booking.Notes);
    }
    [Fact]
    public async Task Configured_customer_questions_are_required_and_saved_with_a_snapshot()
    {
        await using var fixture = await QuotationFixture.CreateAsync(new EmailQueue());
        var token = TestContext.Current.CancellationToken;
        var division = await fixture.Db.Divisions.SingleAsync(token);
        var policy = DivisionBookingFlow.For(division) with { CustomerFields = [new CustomerQuestion("SITE_ACCESS", "Site access", "Choice", true, Options: ["Open site", "Restricted site"])] };
        division.CustomerBookingConfigurationJson = policy.ToJson();
        await fixture.Db.SaveChangesAsync(token);

        var missing = await fixture.Controller.RequestBooking(fixture.ValidRequest(), token);
        Assert.IsType<BadRequestObjectResult>(missing.Result);
        Assert.Empty(await fixture.Db.Bookings.ToListAsync(token));
        var invalid = await fixture.Controller.RequestBooking(fixture.ValidRequest() with { ConfigurationAnswers = new Dictionary<string, string> { ["SITE_ACCESS"] = "Spoofed option" } }, token);
        Assert.IsType<BadRequestObjectResult>(invalid.Result);
        var valid = await fixture.Controller.RequestBooking(fixture.ValidRequest() with { ConfigurationAnswers = new Dictionary<string, string> { ["SITE_ACCESS"] = "Open site" } }, token);
        Assert.IsType<OkObjectResult>(valid.Result);
        var booking = await fixture.Db.Bookings.SingleAsync(token);
        var snapshot = OrganizationConfiguration.Read<BookingConfigurationSnapshot>(booking.ConfigurationSnapshotJson);
        Assert.NotNull(snapshot);
        Assert.Equal("Open site", snapshot.Answers["SITE_ACCESS"]);
        Assert.Equal("Site access", snapshot.Questions[0].Label);
    }

    [Fact]
    public async Task Paused_branch_division_rejects_new_customer_requests()
    {
        await using var fixture = await QuotationFixture.CreateAsync(new EmailQueue());
        var token = TestContext.Current.CancellationToken;
        var link = await fixture.Db.BranchDivisions.SingleAsync(token);
        link.AcceptsBookings = false;
        await fixture.Db.SaveChangesAsync(token);
        var result = await fixture.Controller.RequestBooking(fixture.ValidRequest(), token);
        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await fixture.Db.Bookings.ToListAsync(token));
    }

    private sealed class ThrowingEmailQueue : IEmailQueue
    {
        public OutboundEmail Queue(ApplicationDbContext db, string recipient, string subject,
            string htmlBody, string? textBody = null, string? category = null) =>
            throw new InvalidOperationException("Simulated confirmation failure.");
    }

    private sealed class QuotationFixture : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; }
        public PublicRentalsController Controller { get; }
        private Guid AssetId { get; }

        private QuotationFixture(ApplicationDbContext db, PublicRentalsController controller, Guid assetId)
        {
            Db = db;
            Controller = controller;
            AssetId = assetId;
        }

        public static async Task<QuotationFixture> CreateAsync(IEmailQueue emailQueue)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"quotation-{Guid.NewGuid():N}")
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var branch = new Branch { Code = "LTK", Name = "Lautoka" };
            var division = new Division { Code = "EQUIP", Name = "Equipment", DefaultTaxRate = 0,
                CustomerBookingConfigurationJson = new DivisionBookingFlow(PersonnelRequirement.Required, QuoteMode: "Always").ToJson() };
            var service = new ServiceOffering
            {
                DivisionId = division.Id, Code = "EQUIPMENT_HIRE", Name = "Equipment hire",
                Type = ServiceOfferingType.EquipmentHire, PersonnelRequirement = PersonnelRequirement.Required,
                IsBookableOnline = true, RequiresQuote = true, RequiresDelivery = true,
            };
            var category = new AssetCategory
            {
                DivisionId = division.Id, ServiceOfferingId = service.Id, Code = "HEAVY_MACHINE",
                Name = "Heavy machine", PersonnelRequirement = PersonnelRequirement.Required,
            };
            var asset = new Asset
            {
                AssetNumber = "HE-001", Name = "CAT 320 Hydraulic Excavator", Type = AssetType.HeavyEquipment,
                BranchId = branch.Id, DivisionId = division.Id, ServiceOfferingId = service.Id,
                AssetCategoryId = category.Id, DailyRate = 1180m, RequiresDelivery = true,
                PersonnelRequirement = PersonnelRequirement.Required,
            };
            var customer = new Customer
            {
                CustomerNumber = "CUS-001", Name = "Rakesh Kumar", Email = "rakesh@example.test",
                Phone = "9999999", Type = CustomerType.Individual,
            };
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = customer.Email, Email = customer.Email,
                FullName = customer.Name, CustomerId = customer.Id,
            };

            db.AddRange(branch, division, service, category, asset, customer, user);
            db.BranchDivisions.Add(new BranchDivision { BranchId = branch.Id, DivisionId = division.Id });
            db.BranchDivisionServices.Add(new BranchDivisionService
            {
                BranchId = branch.Id, DivisionId = division.Id, ServiceOfferingId = service.Id,
            });
            db.ChargeDefinitions.Add(new ChargeDefinition
            {
                DivisionId = division.Id, ServiceOfferingId = service.Id, Code = "OPERATOR",
                Name = "Trained operator", Category = ChargeCategory.Operator, Unit = ChargeUnit.Hour,
                DefaultSellingRate = 42m, IsTaxable = false,
            });
            using (db.SuppressNotifications()) await db.SaveChangesAsync();

            var userManager = CreateUserManager(db);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "test"));
            var controller = new PublicRentalsController(db, userManager, emailQueue, new TestEnvironment())
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
            };
            return new QuotationFixture(db, controller, asset.Id);
        }

        public PublicBookingRequest ValidRequest()
        {
            var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
            return new PublicBookingRequest(
                AssetId, start, start.AddDays(6), "Rakesh Kumar", CustomerType.Individual, null,
                "rakesh@example.test", "9999999", "Lautoka", null, "Construction",
                null, "Delivery", "Field 40 Road", null, null, true, 56m,
                null, null, true, null);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
        }

        private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db)
        {
            var store = new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<ApplicationUser,
                IdentityRole<Guid>, ApplicationDbContext, Guid>(db);
            return new UserManager<ApplicationUser>(store, Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(), [], [], new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(), null!, NullLogger<UserManager<ApplicationUser>>.Instance);
        }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "CREMS.Api.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
