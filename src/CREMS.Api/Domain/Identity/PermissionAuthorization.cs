using System.Security.Claims;
using CREMS.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Domain.Identity;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionAuthorizationHandler(ApplicationDbContext db) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.IsInRole(SystemRoles.SuperAdministrator)) { context.Succeed(requirement); return; }
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(id, out var userId)) return;
        var now = DateTimeOffset.UtcNow;
        var direct = await db.UserPermissionOverrides.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.Permission == requirement.Permission && (x.ExpiresAt == null || x.ExpiresAt > now));
        if (direct is not null) { if (direct.IsGranted) context.Succeed(requirement); return; }
        var roles = context.User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray();
        if (await db.RolePermissions.AsNoTracking().AnyAsync(x => roles.Contains(x.RoleName) && x.Permission == requirement.Permission)) context.Succeed(requirement);
    }
}
