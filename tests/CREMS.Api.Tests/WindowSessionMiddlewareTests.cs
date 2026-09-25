using System.Security.Claims;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class WindowSessionMiddlewareTests
{
    [Theory]
    [InlineData("GET", "/api/health", true)]
    [InlineData("GET", "/api/public/assets", true)]
    [InlineData("POST", "/api/customer-account/register", true)]
    [InlineData("POST", "/api/customer-account/activate", true)]
    [InlineData("GET", "/api/bookings", false)]
    public async Task Public_routes_ignore_unrelated_cookie_but_protected_routes_remain_protected(string method, string path, bool allowed)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "staff")], "test"));
        context.Response.Body = new MemoryStream();
        var called = false;
        var middleware = new WindowSessionMiddleware(_ => { called = true; return Task.CompletedTask; });
        await middleware.Invoke(context, new WindowSessionRegistry());
        Assert.Equal(allowed, called);
        Assert.Equal(allowed ? 200 : 401, context.Response.StatusCode);
    }

    [Fact]
    public async Task Malformed_request_does_not_delete_the_shared_authentication_cookie()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/customer-account/session";
        context.Request.Method = "GET";
        context.Request.Headers.Cookie = "CREMS.CustomerSession=stale-ticket";
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "customer"),
            new Claim(ClaimTypes.Role, SystemRoles.Customer),
        ], "test"));
        context.Response.Body = new MemoryStream();

        var middleware = new WindowSessionMiddleware(_ => Task.CompletedTask);
        await middleware.Invoke(context, new WindowSessionRegistry());

        Assert.Equal(401, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Headers.SetCookie.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_window_cannot_revoke_the_active_window(bool missingHeader)
    {
        var registry = new WindowSessionRegistry();
        var activeWindow = Guid.NewGuid().ToString();
        var rejectedWindow = Guid.NewGuid().ToString();
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("ticket")));
        registry.Validate("customer", activeWindow, fingerprint, DateTimeOffset.UtcNow);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/customer-account/session";
        context.Request.Headers.Cookie = "CREMS.CustomerSession=ticket";
        if (!missingHeader) context.Request.Headers["X-CREMS-Window-Id"] = rejectedWindow;
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "customer"),
            new Claim(ClaimTypes.Role, SystemRoles.Customer)], "test"));
        context.Response.Body = new MemoryStream();
        var middleware = new WindowSessionMiddleware(_ => throw new InvalidOperationException("Rejected request was allowed."));
        await middleware.Invoke(context, registry);
        Assert.Equal(401, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Headers.SetCookie.Count);
        Assert.Equal(WindowSessionResult.DifferentWindow, registry.Validate("customer", rejectedWindow, fingerprint, DateTimeOffset.UtcNow));
        Assert.Equal(WindowSessionResult.Valid, registry.Validate("customer", activeWindow, fingerprint, DateTimeOffset.UtcNow));
    }}
