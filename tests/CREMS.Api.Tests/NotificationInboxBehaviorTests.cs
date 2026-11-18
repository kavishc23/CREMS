using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Fixture=CREMS.Api.Tests.CustomerJourneyRegressionTests.Fixture;
namespace CREMS.Api.Tests;
public sealed class NotificationInboxBehaviorTests
{
    private static CancellationToken T=>TestContext.Current.CancellationToken;
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static NotificationsController Controller(Fixture f)=>new(f.Db,f.Users,new CurrentStaffScope(f.Db),f.Context.RequestServices.GetRequiredService<IAuthorizationService>()){ControllerContext=new(){HttpContext=f.Context}};
    [Fact]public async Task Customer_inbox_only_exposes_own_current_messages_and_counts_reads()
    {await using var f=await Fixture.Create(s=>s.AddApplicationAuthorization());var c=Controller(f);var own=new InAppNotification{Audience="Customer",CustomerId=f.Customer.Id,Title="Own update",Message="Collection ready",EventKey="own"};f.Db.AddRange(own,new InAppNotification{Audience="Customer",CustomerId=Guid.NewGuid(),Title="Other customer",Message="Private",EventKey="other"},new InAppNotification{Audience="Customer",CustomerId=f.Customer.Id,Title="Expired",Message="Old",EventKey="expired",ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(-1)});await f.Db.SaveChangesAsync(T);var inbox=Json(Assert.IsType<OkObjectResult>(await c.Customer(99,T)).Value);Assert.Equal(1,inbox.GetProperty("unreadCount").GetInt32());Assert.Equal("Own update",inbox.GetProperty("items")[0].GetProperty("title").GetString());Assert.Equal(1,inbox.GetProperty("page").GetInt32());f.Db.NotificationReads.Add(new(){NotificationId=own.Id,UserId=f.User.Id,ReadAt=DateTimeOffset.UtcNow});await f.Db.SaveChangesAsync(T);inbox=Json(Assert.IsType<OkObjectResult>(await c.Customer(1,T)).Value);Assert.Equal(0,inbox.GetProperty("unreadCount").GetInt32());Assert.True(inbox.GetProperty("items")[0].GetProperty("isRead").GetBoolean());Assert.IsType<BadRequestObjectResult>(await c.CustomerRead(new(Enumerable.Range(0,51).Select(_=>Guid.NewGuid()).ToArray()),T));}
    [Theory][InlineData("Unknown","Immediate")][InlineData("Booking","Weekly")]
    public async Task Invalid_notification_preferences_are_rejected_without_saving(string category,string frequency)
    {await using var f=await Fixture.Create(s=>s.AddApplicationAuthorization());await StaffSessionBehaviorTests.Staff(f,SystemRoles.Administrator);var c=Controller(f);Assert.IsType<BadRequestObjectResult>(await c.SavePreferences([new(category,true,true,false,frequency)],T));Assert.Empty(await f.Db.StaffNotificationPreferences.ToListAsync(T));Assert.IsType<BadRequestObjectResult>(await c.SavePreferences([new("Booking",true,true,false,"Daily"),new("Booking",true,true,false,"Immediate")],T));}
    [Fact]public async Task Email_preferences_require_verification_then_update_existing_rows_without_duplicates()
    {await using var f=await Fixture.Create(s=>s.AddApplicationAuthorization());await StaffSessionBehaviorTests.Staff(f,SystemRoles.Administrator);var c=Controller(f);Assert.IsType<BadRequestObjectResult>(await c.SavePreferences([new("Booking",true,true,true,"Daily")],T));f.User.EmailConfirmed=true;await f.Users.UpdateAsync(f.User);Assert.IsType<NoContentResult>(await c.SavePreferences([new("Booking",false,false,true,"Daily")],T));Assert.IsType<NoContentResult>(await c.SavePreferences([new("Booking",true,true,true,"Immediate")],T));var row=await f.Db.StaffNotificationPreferences.SingleAsync(T);Assert.Equal("Immediate",row.EmailFrequency);Assert.True(row.InAppEnabled);var preferences=Json(Assert.IsType<OkObjectResult>(await c.Preferences(T)).Value);Assert.Equal(NotificationAccess.Categories.Length,preferences.GetArrayLength());}
    [Fact]public async Task Staff_inbox_filters_category_severity_and_unread_messages()
    {await using var f=await Fixture.Create(s=>s.AddApplicationAuthorization());await StaffSessionBehaviorTests.Staff(f,SystemRoles.Administrator);var c=Controller(f);f.Db.InAppNotifications.AddRange(new(){Title="Urgent booking",Message="Check overdue",EventKey="booking",Kind="Booking",Severity="Urgent",RecipientUserId=f.User.Id},new(){Title="Maintenance update",Message="Service due",EventKey="maintenance",Kind="Maintenance",Severity="Info",RecipientUserId=f.User.Id});await f.Db.SaveChangesAsync(T);var filtered=Json(Assert.IsType<OkObjectResult>(await c.Staff("Booking",true,"Urgent",false,1,T)).Value);Assert.Equal(1,filtered.GetProperty("items").GetArrayLength());Assert.Equal("Urgent booking",filtered.GetProperty("items")[0].GetProperty("title").GetString());Assert.False(string.IsNullOrWhiteSpace(c.Response.Headers["X-Notification-Version"]));}
}
