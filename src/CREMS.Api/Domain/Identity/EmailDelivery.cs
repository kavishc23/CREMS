using CREMS.Api.Domain.Common;

namespace CREMS.Api.Domain.Identity;

public sealed class OutboundEmail : Entity
{
    public required string Recipient { get; set; }
    public required string Subject { get; set; }
    public required string HtmlBody { get; set; }
    public string? TextBody { get; set; }
    public EmailDeliveryStatus Status { get; set; } = EmailDeliveryStatus.Queued;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? FailureReason { get; set; }
    public string? Category { get; set; }
}

public sealed class PasswordResetOtp : Entity
{
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public required string CodeHash { get; set; }
    public required string Salt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public string? RequestedIp { get; set; }
}

public sealed class EmailVerificationOtp : Entity
{
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public required string CodeHash { get; set; }
    public required string Salt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public string? RequestedIp { get; set; }
}

public sealed class CustomerAccountActivation : Entity
{
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public required string CodeHash { get; set; }
    public required string Salt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public Guid InvitedByUserId { get; set; }
}

public enum EmailDeliveryStatus { Queued, Sending, Sent, Failed }
