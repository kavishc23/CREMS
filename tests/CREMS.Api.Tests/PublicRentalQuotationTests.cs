using System.Security.Claims;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
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
            var division = new Division { Code = "EQUIP", Name = "Equipment", DefaultTaxRate = 0 };
            var service = new ServiceOffering
            {
                DivisionId = division.Id, Code = "EQUIPMENT_HIRE", Name = "Equipment hire",
                Type = ServiceOfferingType.EquipmentHire, PersonnelRequirement = PersonnelRequirement.Required,
                IsBookableOnline = false, RequiresQuote = true, RequiresDelivery = true,
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
