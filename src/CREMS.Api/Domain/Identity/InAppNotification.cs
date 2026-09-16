using CREMS.Api.Domain.Common;

namespace CREMS.Api.Domain.Identity;

public sealed class InAppNotification : Entity
{
    public string Audience { get; set; } = "Staff";
    public Guid? CustomerId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? DivisionId { get; set; }
    public string? RequiredRole { get; set; }
    public string Kind { get; set; } = "Booking";
    public required string Title { get; set; }
    public required string Message { get; set; }
    public string? Url { get; set; }
    public required string EventKey { get; set; }
    public Guid? SentByUserId { get; set; }
    public Guid? RecipientUserId { get; set; }
    public string Severity { get; set; } = "Info";
    public string? ActionType { get; set; }
    public string? ActionLabel { get; set; }
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class StaffNotificationPreference
{
    public Guid UserId { get; set; }
    public string Category { get; set; } = "Booking";
    public bool InAppEnabled { get; set; } = true;
    public bool ToastEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; }
    public string EmailFrequency { get; set; } = "Immediate";
}

public sealed class NotificationEmailDelivery
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset QueuedAt { get; set; }
}

public sealed class NotificationRead
{
    public Guid NotificationId { get; set; }
    public InAppNotification? Notification { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset ReadAt { get; set; } = DateTimeOffset.UtcNow;
}
