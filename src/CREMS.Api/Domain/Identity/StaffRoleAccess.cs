using System.Security.Claims;
using CREMS.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Domain.Identity;

// These modules use existing CREMS policies and pages. Custom staff stay within their assigned scope.
public sealed record StaffModule(string Key, string Label, string[] Pages);
public static class StaffRoleAccess
{
    public static readonly StaffModule[] Modules = [
        new(SystemPolicies.ViewAssets, "Asset register", ["assets"]),
        new(SystemPolicies.ManageRentals, "Customers, bookings & rentals", ["customers", "bookings", "rentals", "availability"]),
        new(SystemPolicies.ManageMaintenance, "Maintenance workspace", ["maintenance"]),
        new(SystemPolicies.ManageBranch, "Operations centre", ["operations"]),
        new(SystemPolicies.ManageFinance, "Finance", ["finance"]),
        new(SystemPolicies.ViewReports, "Reports & performance", ["reports"]),
        new(SystemPolicies.UseAssetQr, "Scan asset QR", ["scan"]),
    ];
    public static async Task<string[]> RoleNamesAsync(ApplicationDbContext db, CancellationToken token) =>
        await db.Roles.AsNoTracking().Where(role => role.Name != SystemRoles.Customer &&
            (SystemRoles.Staff.Contains(role.Name!) || db.RolePermissions.Any(grant => grant.RoleName == role.Name && grant.Permission.StartsWith("staff.module."))))
            .Select(role => role.Name!).OrderBy(name => name).ToArrayAsync(token);
    public static string Grant(string policy) => $"staff.module.{policy}";
    public static readonly string[] OperationalPermissions = [
        SystemPermissions.RentalsApprove, SystemPermissions.MaintenanceComplete, SystemPermissions.PaymentsRefund,
        SystemPermissions.ReportsFinancial, SystemPermissions.AssetsView, SystemPermissions.AssetsCreate,
        SystemPermissions.AssetsEdit, SystemPermissions.AssetsTransfer, SystemPermissions.AssetsInspect,
        SystemPermissions.AssetsRecordMeter, SystemPermissions.AssetsRetire, SystemPermissions.AssetsViewFinancials
    ];
}
public sealed record StaffPolicyRequirement(string Policy, string[] BuiltInRoles) : IAuthorizationRequirement;
public sealed class StaffPolicyAuthorizationHandler(IServiceProvider services) : AuthorizationHandler<StaffPolicyRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, StaffPolicyRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true || context.User.IsInRole(SystemRoles.Customer)) return;
        if (requirement.BuiltInRoles.Any(context.User.IsInRole)) { context.Succeed(requirement); return; }
        var roles = context.User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray();
        if (roles.Length == 0) return;
        var db = services.GetService<ApplicationDbContext>();
        if (db is null) return;
        if (requirement.Policy == SystemPolicies.StaffPortal)
        {
            if (await db.RolePermissions.AsNoTracking().AnyAsync(grant => roles.Contains(grant.RoleName) && grant.Permission.StartsWith("staff.module.")))
                context.Succeed(requirement);
            return;
        }
        if (!StaffRoleAccess.Modules.Any(module => module.Key == requirement.Policy) && !StaffRoleAccess.OperationalPermissions.Contains(requirement.Policy)) return;
        var permission = StaffRoleAccess.Modules.Any(module => module.Key == requirement.Policy) ? StaffRoleAccess.Grant(requirement.Policy) : requirement.Policy;
        if (StaffRoleAccess.OperationalPermissions.Contains(permission) &&
            Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            var now = DateTimeOffset.UtcNow;
            var direct = await db.UserPermissionOverrides.AsNoTracking().FirstOrDefaultAsync(grant =>
                grant.UserId == userId && grant.Permission == permission && (grant.ExpiresAt == null || grant.ExpiresAt > now));
            if (direct is not null) { if (direct.IsGranted) context.Succeed(requirement); return; }
        }
        if (await db.RolePermissions.AsNoTracking().AnyAsync(grant => roles.Contains(grant.RoleName) &&
            grant.Permission == permission)) context.Succeed(requirement);
    }
}

