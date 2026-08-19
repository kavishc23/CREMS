using CREMS.Api.Domain.Common;

namespace CREMS.Api.Domain.Identity;

public static class SystemPermissions
{
    public const string UsersCreate = "users.create";
    public const string UsersResetPassword = "users.reset_password";
    public const string UsersManageAccess = "users.manage_access";
    public const string CustomersManageAccess = "customers.manage_access";
    public const string RentalsApprove = "rentals.approve";
    public const string MaintenanceComplete = "maintenance.complete";
    public const string PaymentsRefund = "payments.refund";
    public const string ReportsFinancial = "reports.financial";
    public const string DivisionsConfigure = "divisions.configure";
    public const string BranchesConfigure = "branches.configure";
    public const string ServicesConfigure = "services.configure";
    public const string AssetCategoriesConfigure = "asset_categories.configure";
    public const string BranchCalendarManage = "branch_calendar.manage";
    public const string PricingConfigure = "pricing.configure";
    public const string AssetsView = "assets.view";
    public const string AssetsCreate = "assets.create";
    public const string AssetsEdit = "assets.edit";
    public const string AssetsTransfer = "assets.transfer";
    public const string AssetsInspect = "assets.inspect";
    public const string AssetsRecordMeter = "assets.record_meter";
    public const string AssetsRetire = "assets.retire";
    public const string AssetsViewFinancials = "assets.view_financials";
    public static readonly string[] All = [UsersCreate, UsersResetPassword, UsersManageAccess, CustomersManageAccess,
        RentalsApprove, MaintenanceComplete, PaymentsRefund, ReportsFinancial, DivisionsConfigure,
        BranchesConfigure, ServicesConfigure, AssetCategoriesConfigure, BranchCalendarManage, PricingConfigure,
        AssetsView, AssetsCreate, AssetsEdit, AssetsTransfer, AssetsInspect, AssetsRecordMeter, AssetsRetire, AssetsViewFinancials];
}

public enum AccountLifecycleStatus { Invited, ActivationPending, Active, Locked, Suspended, Deactivated }
public enum AccessScopeType { Division, Branch, GroupWide }
public enum SecurityEventType { LoginSucceeded, LoginFailed, Logout, SessionExpired, PasswordResetRequested, PasswordResetCompleted, PasswordChanged, MfaChallengeIssued, MfaSucceeded, MfaFailed, SessionRevoked, AccessChanged, AccountSuspended, AccountDeactivated }

public sealed class UserAccessScope : Entity
{
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public AccessScopeType Type { get; set; }
    public Guid? DivisionId { get; set; }
    public Guid? BranchId { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid GrantedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class RolePermission : Entity
{
    public string RoleName { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;
}

public sealed class UserPermissionOverride : Entity
{
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public string Permission { get; set; } = string.Empty;
    public bool IsGranted { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid GrantedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class SecurityEvent : Entity
{
    public Guid? UserId { get; set; }
    public string? Email { get; set; }
    public SecurityEventType Type { get; set; }
    public bool Succeeded { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? WindowId { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserSession : Entity
{
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public string WindowId { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceLabel { get; set; }
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? EndReason { get; set; }
}

public sealed class MfaChallenge : Entity
{
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public string Purpose { get; set; } = "Login";
}
