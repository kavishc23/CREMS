using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using Microsoft.AspNetCore.Identity;

namespace CREMS.Api.Domain.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;
    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid? DivisionId { get; set; }
    public Division? Division { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTimeOffset? PasswordChangedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public string? AdminNote { get; set; }
    public string? SuspensionReason { get; set; }
    public AccountLifecycleStatus AccountStatus { get; set; } = AccountLifecycleStatus.Active;
    public bool MfaEnabled { get; set; }
    public bool MfaRequired { get; set; }
    public DateTimeOffset? InvitedAt { get; set; }
    public DateTimeOffset? SuspendedAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
    public ICollection<UserAccessScope> AccessScopes { get; set; } = [];
}
