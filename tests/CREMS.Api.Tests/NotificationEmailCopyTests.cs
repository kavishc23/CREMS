using CREMS.Api.Services;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class NotificationEmailCopyTests
{
    [Theory]
    [InlineData("ManualNotification", true)]
    [InlineData("StaffActionNotifications", true)]
    [InlineData("StaffDigest:user:20260928", true)]
    [InlineData("PasswordReset", false)]
    [InlineData("EmailVerification", false)]
    [InlineData(null, false)]
    public void Only_notification_categories_receive_copies(string? category, bool copied)
    {
        var options = new EmailOptions { NotificationCopyTo = "owner@example.test" };
        Assert.Equal(copied ? "owner@example.test" : null, EmailDeliveryWorker.NotificationCopyRecipient(options, "recipient@example.test", category));
    }

    [Fact]
    public void Redirect_and_same_recipient_suppress_copy()
    {
        var options = new EmailOptions { NotificationCopyTo = "owner@example.test" };
        Assert.Null(EmailDeliveryWorker.NotificationCopyRecipient(options, "OWNER@example.test", "ManualNotification"));
        options.RedirectAllTo = "test@example.test";
        Assert.Null(EmailDeliveryWorker.NotificationCopyRecipient(options, "test@example.test", "ManualNotification"));
        options.NotificationDirectDelivery = true;
        Assert.Equal("owner@example.test", EmailDeliveryWorker.NotificationCopyRecipient(options, "recipient@example.test", "ManualNotification"));
        Assert.Null(EmailDeliveryWorker.NotificationCopyRecipient(options, "recipient@example.test", "PasswordReset"));
    }
}
