using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Fixture=CREMS.Api.Tests.CustomerJourneyRegressionTests.Fixture;
namespace CREMS.Api.Tests;
public sealed class StaffSessionBehaviorTests
{
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
    internal static async Task Staff(Fixture f,string role=SystemRoles.RentalOfficer)
    {
        f.Context.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>().Context=f.Context;
        await f.Users.RemoveFromRoleAsync(f.User,SystemRoles.Customer);
        var manager=f.Context.RequestServices.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if(!await manager.RoleExistsAsync(role))Assert.True((await manager.CreateAsync(new(role))).Succeeded);
        Assert.True((await f.Users.AddToRoleAsync(f.User,role)).Succeeded);
        f.Context.User=await f.Context.RequestServices.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(f.User);
    }
    [Fact] public async Task Valid_staff_login_records_window_device_and_security_event_then_logout_ends_session()
    {
        await using var f=await Fixture.Create();await Staff(f);f.Context.Request.Headers.UserAgent="Test Mobile browser";
        Assert.IsType<NoContentResult>(await f.Login.Login(new(f.User.Email!,Fixture.Password)));
        var session=await f.Db.UserSessions.SingleAsync(TestContext.Current.CancellationToken);Assert.Equal("Mobile browser",session.DeviceLabel);Assert.Equal(f.Context.Request.Headers["X-CREMS-Window-Id"].ToString(),session.WindowId);Assert.NotNull(f.User.LastLoginAt);
        Assert.IsType<OkObjectResult>(await f.Login.SecurityHistory(TestContext.Current.CancellationToken));
        Assert.IsType<NoContentResult>(await f.Login.Logout());Assert.NotNull(session.EndedAt);Assert.Equal("Logout",session.EndReason);Assert.Contains(await f.Db.SecurityEvents.ToListAsync(TestContext.Current.CancellationToken),x=>x.Type==SecurityEventType.LoginSucceeded);
    }
    [Fact] public async Task Wrong_password_and_unknown_account_share_unauthorized_result_and_audit_failures()
    {await using var f=await Fixture.Create();await Staff(f);Assert.IsType<UnauthorizedResult>(await f.Login.Login(new(f.User.Email!,"InvalidPassword1!")));Assert.IsType<UnauthorizedResult>(await f.Login.Login(new("unknown@example.test",Fixture.Password)));Assert.Equal(2,await f.Db.SecurityEvents.CountAsync(x=>x.Type==SecurityEventType.LoginFailed,TestContext.Current.CancellationToken));}
    [Theory] [InlineData(AccountLifecycleStatus.Suspended)] [InlineData(AccountLifecycleStatus.Deactivated)]
    public async Task Disabled_lifecycle_accounts_cannot_start_a_session(AccountLifecycleStatus status)
    {await using var f=await Fixture.Create();await Staff(f);f.User.AccountStatus=status;await f.Users.UpdateAsync(f.User);Assert.IsType<UnauthorizedResult>(await f.Login.Login(new(f.User.Email!,Fixture.Password)));Assert.Empty(await f.Db.UserSessions.ToListAsync(TestContext.Current.CancellationToken));}
    [Fact] public async Task Administrator_MFA_rejects_bad_code_and_resend_throttles_repeated_requests()
    {await using var f=await Fixture.Create();await Staff(f,SystemRoles.Administrator);var response=Assert.IsType<AcceptedResult>(await f.Login.Login(new(f.User.Email!,Fixture.Password)));Assert.True(Json(response.Value).GetProperty("requiresMfa").GetBoolean());var challenge=await f.Db.MfaChallenges.SingleAsync(TestContext.Current.CancellationToken);Assert.IsType<UnauthorizedResult>(await f.Login.VerifyMfa(new(challenge.Id,"invalid")));Assert.Equal(1,challenge.FailedAttempts);Assert.Equal(429,Assert.IsType<ObjectResult>(await f.Login.ResendMfa(challenge.Id)).StatusCode);Assert.Empty(await f.Db.UserSessions.ToListAsync(TestContext.Current.CancellationToken));challenge.CreatedAt=DateTimeOffset.UtcNow.AddMinutes(-1);await f.Db.SaveChangesAsync(TestContext.Current.CancellationToken);Assert.IsType<AcceptedResult>(await f.Login.ResendMfa(challenge.Id));Assert.NotNull(challenge.UsedAt);Assert.Equal(2,await f.Db.MfaChallenges.CountAsync(TestContext.Current.CancellationToken));}
    [Fact] public async Task Super_administrator_uses_the_current_password_only_policy_even_with_MFA_flags()
    {await using var f=await Fixture.Create();await Staff(f,SystemRoles.SuperAdministrator);f.User.MfaEnabled=true;f.User.MfaRequired=true;await f.Users.UpdateAsync(f.User);Assert.IsType<NoContentResult>(await f.Login.Login(new(f.User.Email!,Fixture.Password)));Assert.Empty(await f.Db.MfaChallenges.ToListAsync(TestContext.Current.CancellationToken));}
    [Fact] public async Task Unknown_challenge_is_rejected_and_resend_does_not_disclose_it()
    {await using var f=await Fixture.Create();Assert.IsType<UnauthorizedResult>(await f.Login.VerifyMfa(new(Guid.NewGuid(),"123456")));Assert.IsType<AcceptedResult>(await f.Login.ResendMfa(Guid.NewGuid()));}
    [Fact] public async Task Session_response_contains_staff_identity_and_updates_activity()
    {await using var f=await Fixture.Create();await Staff(f);f.User.LastActivityAt=null;var session=Assert.IsType<SessionResponse>(Assert.IsType<OkObjectResult>((await f.Login.GetSession()).Result).Value);Assert.Equal(f.User.Id,session.Id);Assert.Contains(SystemRoles.RentalOfficer,session.Roles);Assert.Contains("dashboard",session.Pages!);Assert.NotNull(f.User.LastActivityAt);f.User.IsActive=false;await f.Users.UpdateAsync(f.User);Assert.IsType<UnauthorizedResult>((await f.Login.GetSession()).Result);}
    [Fact] public async Task Password_change_requires_current_password_and_a_different_strong_replacement()
    {await using var f=await Fixture.Create();await Staff(f);Assert.IsAssignableFrom<ObjectResult>(await f.Login.RequestPasswordChange(new("wrong", "NewTrainingPassword1!")));Assert.Empty(await f.Db.MfaChallenges.ToListAsync(TestContext.Current.CancellationToken));f.Login.ModelState.Clear();Assert.IsAssignableFrom<ObjectResult>(await f.Login.RequestPasswordChange(new(Fixture.Password,Fixture.Password)));f.Login.ModelState.Clear();Assert.IsAssignableFrom<ObjectResult>(await f.Login.RequestPasswordChange(new(Fixture.Password,"weak")));f.Login.ModelState.Clear();Assert.IsType<AcceptedResult>(await f.Login.RequestPasswordChange(new(Fixture.Password,"NewTrainingPassword1!")));var challenge=await f.Db.MfaChallenges.SingleAsync(TestContext.Current.CancellationToken);Assert.Equal("PasswordChange",challenge.Purpose);Assert.IsAssignableFrom<ObjectResult>(await f.Login.ConfirmPasswordChange(new(challenge.Id,"wrong",Fixture.Password,"NewTrainingPassword1!")));Assert.Equal(1,challenge.FailedAttempts);Assert.True(await f.Users.CheckPasswordAsync(f.User,Fixture.Password));}
    [Fact] public async Task Session_revocation_only_accepts_sessions_owned_by_the_current_user()
    {await using var f=await Fixture.Create();await Staff(f);Assert.IsType<NoContentResult>(await f.Login.Login(new(f.User.Email!,Fixture.Password)));var session=await f.Db.UserSessions.SingleAsync(TestContext.Current.CancellationToken);Assert.IsType<NotFoundResult>(await f.Login.RevokeOwnSession(Guid.NewGuid(),TestContext.Current.CancellationToken));Assert.IsType<NoContentResult>(await f.Login.RevokeOwnSession(session.Id,TestContext.Current.CancellationToken));Assert.Equal("Revoked by user",session.EndReason);Assert.NotNull(session.EndedAt);}
}
