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
    public async Task Invalid_protected_window_session_expires_the_authentication_cookie()
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
        Assert.Contains(context.Response.Headers.SetCookie,
            value => value?.StartsWith("CREMS.CustomerSession=", StringComparison.Ordinal) == true &&
                     value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }
}
