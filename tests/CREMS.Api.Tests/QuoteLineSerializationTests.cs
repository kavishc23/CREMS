using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class QuoteLineSerializationTests
{
    [Fact]
    public void Reads_existing_string_enum_quote_lines()
    {
        const string storedByPublicQuotation = """[{"Description":"Concrete Mixer hire","Quantity":7,"Rate":250,"Unit":"Day","Category":"BaseHire"}]""";

        var lines = QuoteLineSerialization.Deserialize(storedByPublicQuotation);

        var line = Assert.Single(lines);
        Assert.Equal(ChargeUnit.Day, line.Unit);
        Assert.Equal(ChargeCategory.BaseHire, line.Category);
    }

    [Fact]
    public void Continues_to_read_legacy_numeric_enum_quote_lines()
    {
        const string legacyLine = """[{"Description":"Transport","Quantity":1,"Rate":100,"Unit":5,"Category":3}]""";

        var line = Assert.Single(QuoteLineSerialization.Deserialize(legacyLine));

        Assert.Equal(ChargeUnit.Trip, line.Unit);
        Assert.Equal(ChargeCategory.Transport, line.Category);
    }

    [Fact]
    public void Writes_quote_lines_with_string_enums()
    {
        var json = QuoteLineSerialization.Serialize([new QuoteLine("Hire", 1, 500, ChargeUnit.Day, Category: ChargeCategory.BaseHire)]);

        Assert.Contains("\"unit\":\"Day\"", json);
        Assert.Contains("\"category\":\"BaseHire\"", json);
    }

    [Fact]
    public async Task Sends_an_existing_public_quote_with_string_enum_lines()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"quote-send-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options);
        var branch = new Branch { Code = "LAB", Name = "Labasa" };
        var division = new Division { Code = "CARPTRAC", Name = "Carptrac" };
        var customer = new Customer { CustomerNumber = "CUS-TEST", Name = "Rakesh Kumar", Email = "rakesh@example.test" };
        var officer = new ApplicationUser { Id = Guid.NewGuid(), UserName = "officer@example.test", Email = "officer@example.test", FullName = "Labasa officer", BranchId = branch.Id, DivisionId = division.Id };
        var quote = new SalesQuote
        {
            QuoteNumber = "QUO-EXISTING", CustomerId = customer.Id, BranchId = branch.Id, DivisionId = division.Id,
            ValidUntil = DateTimeOffset.UtcNow.AddDays(7), Total = 1750m, Subtotal = 1750m,
            LineItemsJson = """[{"Description":"Concrete Mixer hire","Quantity":7,"Rate":250,"Unit":"Day","Category":"BaseHire"}]""",
        };
        db.AddRange(branch, division, customer, officer, quote);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, officer.Id.ToString()),
            new Claim(ClaimTypes.Role, SystemRoles.RentalOfficer),
        ], "test"));
        var controller = new CorporateOperationsController(db, new CurrentStaffScope(db), new EmailQueue(), new AllowAuthorization())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
        };

        var result = await controller.SendQuote(quote.Id, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(QuoteStatus.Sent, quote.Status);
        Assert.Equal("rakesh@example.test", quote.LastEmailedTo);
        Assert.Equal(1, await db.OutboundEmails.CountAsync(TestContext.Current.CancellationToken));
    }

    private sealed class AllowAuthorization : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Success());
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            Task.FromResult(AuthorizationResult.Success());
    }
}
