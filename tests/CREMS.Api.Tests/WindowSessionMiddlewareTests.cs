using System.Security.Claims;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class WindowSessionMiddlewareTests
{
    [Theory]
    [InlineData("/api/health", true)]
    [InlineData("/api/public/assets", true)]
    [InlineData("/api/bookings", false)]
    public async Task Public_reads_ignore_unrelated_staff_cookie_but_staff_routes_remain_protected(string path, bool allowed)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "GET";
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "staff")], "test"));
        context.Response.Body = new MemoryStream();
        var called = false;
        var middleware = new WindowSessionMiddleware(_ => { called = true; return Task.CompletedTask; });
        await middleware.Invoke(context, new WindowSessionRegistry());
        Assert.Equal(allowed, called);
        Assert.Equal(allowed ? 200 : 401, context.Response.StatusCode);
    }
}
