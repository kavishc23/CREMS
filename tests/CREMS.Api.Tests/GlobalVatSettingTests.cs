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

public sealed class GlobalVatSettingTests
{
    [Theory]
    [InlineData("15", true)]
    [InlineData("12.5", true)]
    [InlineData("0", true)]
    [InlineData("-1", false)]
    [InlineData("101", false)]
    [InlineData("invalid", false)]
    public async Task Global_vat_updates_divisions_atomically_but_preserves_existing_bookings(string value, bool valid)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var division = new Division { Code = "MOTORS", Name = "Motors", DefaultTaxRate = 12.5m };
        var setting = new SystemSetting { Key = "rentals.vatRate", Value = "12.5", Category = "Rental" };
        var booking = new Booking { BookingNumber = "EXISTING", TaxRate = 12.5m };
        db.AddRange(division, setting, booking);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = new AdministrationController(db, new CurrentStaffScope(db), null!, null!)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var result = await controller.SaveSetting(setting.Key, new SettingRequest(value), TestContext.Current.CancellationToken);
        if (valid)
        {
            Assert.IsType<NoContentResult>(result);
            Assert.Equal(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), division.DefaultTaxRate);
            Assert.Equal(value, setting.Value);
        }
        else
        {
            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(12.5m, division.DefaultTaxRate);
            Assert.Equal("12.5", setting.Value);
        }
        Assert.Equal(12.5m, booking.TaxRate);
    }
}
