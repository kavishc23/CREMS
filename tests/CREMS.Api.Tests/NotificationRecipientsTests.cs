using System.Security.Claims;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Services;
using Microsoft.Extensions.Options;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class NotificationRecipientsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selected_recipients_receive_one_message_each_and_retries_do_not_duplicate(bool staff)
    {
        await using var db = CreateDatabase();
        var controller = await CreateController(db);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        if (staff)
        {
            var role = new IdentityRole<Guid>(SystemRoles.Administrator) { Id = Guid.NewGuid() };
            db.Roles.Add(role);
            foreach (var id in new[] { first, second })
            {
                db.Users.Add(new ApplicationUser { Id = id, FullName = "Recipient", IsActive = true });
                db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = id, RoleId = role.Id });
            }
        }
        else
            foreach (var id in new[] { first, second })
                db.Customers.Add(new Customer { Id = id, CustomerNumber = id.ToString(), Name = "Recipient" });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var requestId = Guid.NewGuid();
        async Task<ActionResult> Send() => staff
            ? await controller.SendStaff(new SendStaffNotification(requestId, null, null, null, null, "Hello", "A message", RecipientUserIds: [first, second, first]), TestContext.Current.CancellationToken)
            : await controller.Send(new SendNotification(requestId, null, false, "Hello", "A message", [first, second, first]), TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(await Send());
        Assert.IsType<OkObjectResult>(await Send());
        var notices = await db.InAppNotifications.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, notices.Count);
        Assert.Equal(2, notices.Select(x => staff ? x.RecipientUserId : x.CustomerId).Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_selection_sends_nothing(bool staff)
    {
        await using var db = CreateDatabase();
        var controller = await CreateController(db);
        var result = staff
            ? await controller.SendStaff(new SendStaffNotification(Guid.NewGuid(), null, null, null, null, "Hello", "Message", RecipientUserIds: [Guid.NewGuid()]), TestContext.Current.CancellationToken)
            : await controller.Send(new SendNotification(Guid.NewGuid(), null, false, "Hello", "Message", [Guid.NewGuid()]), TestContext.Current.CancellationToken);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(db.InAppNotifications);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Combined_delivery_validates_every_recipient_before_saving(bool invalidStaff, bool invalidCustomer)
    {
        await using var db = CreateDatabase();
        var controller = await CreateController(db);
        var customer = new Customer { Id = Guid.NewGuid(), CustomerNumber = "C1", Name = "Customer" };
        var staff = new ApplicationUser { Id = Guid.NewGuid(), FullName = "Staff", IsActive = true };
        var role = new IdentityRole<Guid>(SystemRoles.Administrator) { Id = Guid.NewGuid() };
        db.AddRange(customer, staff, role);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = staff.Id, RoleId = role.Id });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var request = new SendNotification(Guid.NewGuid(), null, false, "Shared title", "Shared message",
            [invalidCustomer ? Guid.NewGuid() : customer.Id], [invalidStaff ? Guid.NewGuid() : staff.Id]);
        var result = await controller.Send(request, TestContext.Current.CancellationToken);
        if (invalidStaff || invalidCustomer)
        {
            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Empty(db.InAppNotifications);
            return;
        }
        Assert.IsType<OkObjectResult>(result);
        Assert.IsType<OkObjectResult>(await controller.Send(request, TestContext.Current.CancellationToken));
        var notices = await db.InAppNotifications.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, notices.Count);
        Assert.Contains(notices, x => x.Audience == "Customer" && x.CustomerId == customer.Id);
        Assert.Contains(notices, x => x.Audience == "Staff" && x.RecipientUserId == staff.Id);
        Assert.All(notices, x => Assert.Equal("Shared message", x.Message));
    }

    [Theory]
    [InlineData(true, true, true, true, "Immediate", 2)]
    [InlineData(false, true, true, true, "Immediate", 0)]
    [InlineData(true, false, true, true, "Immediate", 0)]
    [InlineData(true, true, false, true, "Immediate", 1)]
    [InlineData(true, true, true, false, "Immediate", 1)]
    [InlineData(true, true, true, true, "Daily", 1)]
    public async Task Email_copies_respect_configuration_verification_preferences_and_retries(
        bool enabled, bool requested, bool verified, bool staffOptIn, string frequency, int expected)
    {
        await using var db = CreateDatabase();
        var controller = await CreateController(db, enabled);
        var customer = new Customer { Id = Guid.NewGuid(), CustomerNumber = "EMAIL", Name = "Customer" };
        var account = new ApplicationUser { Id = Guid.NewGuid(), FullName = "Customer", CustomerId = customer.Id, IsActive = true, Email = "customer@example.test", EmailConfirmed = verified };
        var staff = new ApplicationUser { Id = Guid.NewGuid(), FullName = "Staff", IsActive = true, Email = "staff@example.test", EmailConfirmed = true };
        var role = new IdentityRole<Guid>(SystemRoles.Administrator) { Id = Guid.NewGuid() };
        db.AddRange(customer, account, staff, role);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = staff.Id, RoleId = role.Id });
        db.StaffNotificationPreferences.Add(new StaffNotificationPreference { UserId = staff.Id, Category = "Announcement", EmailEnabled = staffOptIn, EmailFrequency = frequency });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var request = new SendNotification(Guid.NewGuid(), null, false, "Update", "<script>unsafe</script>", [customer.Id], [staff.Id], requested);
        Assert.IsType<OkObjectResult>(await controller.Send(request, TestContext.Current.CancellationToken));
        Assert.IsType<OkObjectResult>(await controller.Send(request, TestContext.Current.CancellationToken));
        Assert.Equal(2, await db.InAppNotifications.CountAsync(TestContext.Current.CancellationToken));
        var emails = await db.OutboundEmails.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expected, emails.Count);
        Assert.Equal(expected, await db.NotificationEmailDeliveries.CountAsync(TestContext.Current.CancellationToken));
        Assert.All(emails, email => {
            Assert.Equal(EmailDeliveryStatus.Queued, email.Status);
            Assert.DoesNotContain("<script>", email.HtmlBody);
            Assert.Contains("&lt;script&gt;", email.HtmlBody);
        });
    }

    private static ApplicationDbContext CreateDatabase() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task<NotificationsController> CreateController(ApplicationDbContext db, bool emailEnabled = false)
    {
        var sender = new ApplicationUser { Id = Guid.NewGuid(), FullName = "Sender", IsActive = true };
        db.Users.Add(sender);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, sender.Id.ToString()), new Claim(ClaimTypes.Role, SystemRoles.Administrator)
        ], "Test")) };
        return new NotificationsController(db, null!, new CurrentStaffScope(db), null!, Options.Create(new EmailOptions { Enabled = emailEnabled })) { ControllerContext = new ControllerContext { HttpContext = context } };
    }
}
