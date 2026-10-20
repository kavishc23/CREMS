using System.Security.Claims;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class DivisionConfigurationApiTests
{
    [Fact]
    public async Task Saving_basic_details_does_not_erase_booking_policy_or_financial_defaults()
    {
        var token = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"division-config-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync(token);
        var division = new Division { Code = "TEST", Name = "Test division" };
        var admin = new ApplicationUser { Id = Guid.NewGuid(), UserName = "admin@example.test",
            Email = "admin@example.test", FullName = "Super Admin" };
        db.AddRange(division, admin);
        await db.SaveChangesAsync(token);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new Claim(ClaimTypes.Role, SystemRoles.SuperAdministrator)], "test"));
        var controller = new DivisionsController(db, new CurrentStaffScope(db))
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } } };
        var flow = new DivisionBookingFlow(PersonnelRequirement.Required, "Always", MaximumHireDays: 30);

        Assert.IsType<OkObjectResult>((await controller.SaveBookingFlow(division.Id,
            new SaveDivisionBookingFlowRequest(flow, 900, 12.5m, "FJD", "Return clean"), token)));
        Assert.IsType<OkObjectResult>((await controller.UpdateDetails(division.Id,
            new SaveDivisionDetailsRequest("TEST", "Updated division", null, null, null,
                DivisionCapabilities.Rental, true, true, "{}"), token)).Result);

        var saved = await db.Divisions.SingleAsync(token);
        Assert.Equal("Updated division", saved.Name);
        Assert.Equal(900, saved.DefaultBondAmount);
        Assert.Equal("Return clean", saved.DefaultRentalTerms);
        Assert.Equal(PersonnelRequirement.Required, DivisionBookingFlow.For(saved).PersonnelRequirement);
        Assert.Equal("Always", DivisionBookingFlow.For(saved).QuoteMode);
        Assert.Equal(30, DivisionBookingFlow.For(saved).MaximumHireDays);
    }

    [Fact]
    public async Task Invalid_policy_is_rejected_before_persistence()
    {
        var token = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"division-invalid-{Guid.NewGuid():N}").Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync(token);
        var division = new Division { Code = "TEST", Name = "Test division" };
        var admin = new ApplicationUser { Id = Guid.NewGuid(), UserName = "admin@example.test",
            Email = "admin@example.test", FullName = "Super Admin" };
        db.AddRange(division, admin);
        await db.SaveChangesAsync(token);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new Claim(ClaimTypes.Role, SystemRoles.SuperAdministrator)], "test"));
        var controller = new DivisionsController(db, new CurrentStaffScope(db))
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } } };

        Assert.IsType<BadRequestObjectResult>((await controller.SaveBookingFlow(division.Id,
            new SaveDivisionBookingFlowRequest(new DivisionBookingFlow(MaximumHireDays: 0), 0, 12.5m, "FJD", null), token)));
        Assert.Equal("{}", (await db.Divisions.SingleAsync(token)).CustomerBookingConfigurationJson);
    }
}
