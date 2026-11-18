using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Fixture=CREMS.Api.Tests.CustomerJourneyRegressionTests.Fixture;
namespace CREMS.Api.Tests;
public sealed class ActionNotificationWorkerBehaviorTests
{
    [Fact]
    public async Task Background_cycle_queues_due_actions_and_customer_emails_without_duplicate_delivery()
    {
        var token=TestContext.Current.CancellationToken;
        await using var f=await Fixture.Create(services=>{services.Configure<EmailOptions>(o=>o.Enabled=true);services.AddSingleton<IEmailQueue,EmailQueue>();services.AddApplicationAuthorization();});
        await f.Users.RemoveFromRoleAsync(f.User,SystemRoles.Customer);var roles=f.Context.RequestServices.GetRequiredService<RoleManager<IdentityRole<Guid>>>();Assert.True((await roles.CreateAsync(new(SystemRoles.Administrator))).Succeeded);Assert.True((await f.Users.AddToRoleAsync(f.User,SystemRoles.Administrator)).Succeeded);f.User.EmailConfirmed=true;await f.Users.UpdateAsync(f.User);
        var now=DateTimeOffset.UtcNow;f.Asset.NextServiceDate=MaintenanceRules.LocalDate(now);f.Asset.Status=AssetStatus.Maintenance;
        var pickup=f.Booking(f.Customer.Id);pickup.Status=BookingStatus.Confirmed;pickup.DepositRequired=100;pickup.Items.Single().StartAt=now.AddHours(1);pickup.Items.Single().EndAt=now.AddDays(2);
        var returning=f.Booking(f.Customer.Id);returning.Status=BookingStatus.ConvertedToRental;returning.Items.Single().StartAt=now.AddDays(-3);returning.Items.Single().EndAt=now.AddHours(-1);
        var quote=new SalesQuote{QuoteNumber="WORKER-Q",CustomerId=f.Customer.Id,BranchId=f.Branch.Id,Status=QuoteStatus.Sent,ValidUntil=now.AddHours(1)};
        var invoice=new RentalInvoice{InvoiceNumber="WORKER-I",BookingId=returning.Id,Status=InvoiceStatus.Issued,IssuedAt=now.AddDays(-20),Total=100,BalanceDue=100,LineItemsJson="[]"};
        var approval=new ApprovalRequest{RequestNumber="WORKER-A",BranchId=f.Branch.Id,EntityType=nameof(Booking),EntityId=pickup.Id,Reason="Training approval",CreatedAt=now.AddDays(-2),StageDecisions=[new(){StageNumber=1,StageName="Manager review",AssignedRole=SystemRoles.BranchManager}]};
        f.Db.AddRange(pickup,returning,quote,invoice,approval,new StaffNotificationPreference{UserId=f.User.Id,Category="Booking",EmailEnabled=true});
        foreach(var key in new[]{"quotationExpiry","pickup","overdueReturn","invoiceDue"})f.Db.NotificationTemplates.Add(new(){Key="reminder."+key,Name="Training "+key,Channel="Email",Subject="Training {{bookingNumber}} {{quoteNumber}} {{invoiceNumber}}",Body="Due {{dueAt}} {{branchName}} {{balanceDue}}"});
        using(f.Db.SuppressNotifications())await f.Db.SaveChangesAsync(token);
        var factory=f.Context.RequestServices.GetRequiredService<IServiceScopeFactory>();
        async Task Cycle()
        {
            using var worker=new ActionNotificationWorker(factory,NullLogger<ActionNotificationWorker>.Instance);
            await worker.StartAsync(token);var deadline=DateTimeOffset.UtcNow.AddSeconds(45);
            while(DateTimeOffset.UtcNow<deadline&&!await f.Db.OutboundEmails.AnyAsync(x=>x.Category=="StaffActionNotifications",token))await Task.Delay(100,token);
            await worker.StopAsync(token);
            Assert.Null(worker.ExecuteTask?.Exception);
        }
        await Cycle();
        var notices=await f.Db.InAppNotifications.AsNoTracking().ToListAsync(token);
        Assert.Contains(notices,x=>x.Title=="Pickup due within two hours");Assert.Contains(notices,x=>x.Title=="Rental return overdue");Assert.Contains(notices,x=>x.Title=="Refundable bond outstanding");Assert.Contains(notices,x=>x.Title=="Allocated asset unavailable");Assert.Contains(notices,x=>x.Title=="Approval deadline passed");Assert.Contains(notices,x=>x.Title=="Asset maintenance due");
        var emails=await f.Db.OutboundEmails.AsNoTracking().ToListAsync(token);Assert.Contains(emails,x=>x.Category!.StartsWith("reminder.pickup:"));Assert.Contains(emails,x=>x.Category!.StartsWith("reminder.quotationExpiry:"));Assert.Contains(emails,x=>x.Category!.StartsWith("reminder.overdueReturn:"));Assert.Contains(emails,x=>x.Category=="StaffActionNotifications");Assert.NotEmpty(await f.Db.NotificationEmailDeliveries.ToListAsync(token));
        var emailCount=emails.Count;var noticeCount=notices.Count;
        // A new worker follows the same startup/cycle path; the stored delivery markers remain authoritative.
        using(var worker=new ActionNotificationWorker(factory,NullLogger<ActionNotificationWorker>.Instance)){await worker.StartAsync(token);await Task.Delay(TimeSpan.FromSeconds(17),token);await worker.StopAsync(token);Assert.Null(worker.ExecuteTask?.Exception);}
        Assert.Equal(emailCount,await f.Db.OutboundEmails.CountAsync(token));Assert.Equal(noticeCount,await f.Db.InAppNotifications.CountAsync(token));
    }
}
