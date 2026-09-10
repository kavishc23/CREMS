using CREMS.Api.Services;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class WindowSessionRegistryTests
{
    [Fact]
    public void Shared_cookie_is_rejected_in_another_window()
    {
        var registry = new WindowSessionRegistry(); var now = DateTimeOffset.UtcNow;
        Assert.Equal(WindowSessionResult.Valid, registry.Validate("user-1", "window-1", "ticket-a", now));
        Assert.Equal(WindowSessionResult.DifferentWindow, registry.Validate("user-1", "window-2", "ticket-a", now.AddMinutes(1)));
    }

    [Fact]
    public void New_login_ticket_moves_session_to_new_window()
    {
        var registry = new WindowSessionRegistry(); var now = DateTimeOffset.UtcNow;
        registry.Validate("user-1", "window-1", "ticket-a", now);
        Assert.Equal(WindowSessionResult.Valid, registry.Validate("user-1", "window-2", "ticket-b", now.AddMinutes(1)));
        Assert.Equal(WindowSessionResult.DifferentWindow, registry.Validate("user-1", "window-1", "ticket-b", now.AddMinutes(2)));
    }

    [Fact]
    public void Idle_session_expires()
    {
        var registry = new WindowSessionRegistry(); var now = DateTimeOffset.UtcNow;
        registry.Validate("user-1", "window-1", "ticket-a", now);
        Assert.Equal(WindowSessionResult.Expired, registry.Validate("user-1", "window-1", "ticket-a", now.AddMinutes(15)));
    }

    [Fact]
    public void Activity_extends_the_idle_session()
    {
        var registry = new WindowSessionRegistry(); var now = DateTimeOffset.UtcNow;
        Assert.Equal(WindowSessionResult.Valid, registry.Validate("user-1", "window-1", "ticket-a", now));
        Assert.Equal(WindowSessionResult.Valid, registry.Validate("user-1", "window-1", "ticket-a", now.AddMinutes(10)));
        Assert.Equal(WindowSessionResult.Valid, registry.Validate("user-1", "window-1", "ticket-a", now.AddMinutes(20)));
    }

    [Fact]
    public void Logout_ends_the_registered_window_session()
    {
        var registry = new WindowSessionRegistry(); var now = DateTimeOffset.UtcNow;
        registry.Validate("user-1", "window-1", "ticket-a", now);

        registry.End("user-1");

        Assert.Equal(WindowSessionResult.Valid, registry.Validate("user-1", "window-2", "ticket-a", now.AddMinutes(1)));
    }
}
