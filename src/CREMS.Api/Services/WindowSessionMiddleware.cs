using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace CREMS.Api.Services;

public sealed class WindowSessionRegistry
{
    private readonly ConcurrentDictionary<string, WindowSession> sessions = new();

    public WindowSessionResult Validate(string userId, string windowId, string ticketFingerprint, DateTimeOffset now)
    {
        while (true)
        {
            if (!sessions.TryGetValue(userId, out var current))
            {
                if (sessions.TryAdd(userId, new(windowId, ticketFingerprint, now, now))) return WindowSessionResult.Valid;
                continue;
            }

            if (now - current.LastSeenAt >= TimeSpan.FromMinutes(15))
            {
                sessions.TryRemove(new KeyValuePair<string, WindowSession>(userId, current));
                return WindowSessionResult.Expired;
            }

            if (current.WindowId == windowId)
            {
                sessions.TryUpdate(userId, current with { TicketFingerprint = ticketFingerprint, LastSeenAt = now }, current);
                return WindowSessionResult.Valid;
            }

            if (current.TicketFingerprint == ticketFingerprint) return WindowSessionResult.DifferentWindow;

            if (sessions.TryUpdate(userId, new(windowId, ticketFingerprint, now, now), current))
                return WindowSessionResult.Valid;
        }
    }

    public void End(string userId) => sessions.TryRemove(userId, out _);

    private sealed record WindowSession(string WindowId, string TicketFingerprint, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt);
}

public enum WindowSessionResult { Valid, Expired, DifferentWindow }

public sealed class WindowSessionMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context, WindowSessionRegistry registry)
    {
        // Public catalogue resources must remain readable even when the request also
        // carries a staff authentication cookie. Browser image requests cannot add
        // the X-CREMS-Window-Id header used by protected API calls.
        if (context.User.Identity?.IsAuthenticated != true ||
            context.Request.Path.StartsWithSegments("/api/public") ||
            context.Request.Path.StartsWithSegments("/api/auth/login"))
        {
            await next(context);
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var windowId = context.Request.Headers["X-CREMS-Window-Id"].ToString();
        var cookieName = context.User.IsInRole(Domain.Identity.SystemRoles.Customer)
            ? "CREMS.CustomerSession"
            : "CREMS.Session";
        var ticket = context.Request.Cookies[cookieName];
        if (string.IsNullOrWhiteSpace(userId) || !Guid.TryParse(windowId, out _) || string.IsNullOrWhiteSpace(ticket))
        {
            await Reject(context, "A valid browser-window session is required.");
            return;
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ticket)));
        var result = registry.Validate(userId, windowId, fingerprint, DateTimeOffset.UtcNow);
        if (result != WindowSessionResult.Valid)
        {
            await Reject(context, result == WindowSessionResult.Expired
                ? "Your session expired. Sign in again."
                : "This session is active in another browser window. Sign in here to continue.");
            return;
        }

        await next(context);
    }

    private static async Task Reject(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { title = "Session unavailable", detail });
    }
}
