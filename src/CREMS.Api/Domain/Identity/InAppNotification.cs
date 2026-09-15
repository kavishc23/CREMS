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
}

public sealed class NotificationRead
{
    public Guid NotificationId { get; set; }
    public InAppNotification? Notification { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset ReadAt { get; set; } = DateTimeOffset.UtcNow;
}
