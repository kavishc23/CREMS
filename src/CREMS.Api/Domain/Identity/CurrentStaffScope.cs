using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using CREMS.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Domain.Identity;

public sealed class CurrentStaffScope(UserManager<ApplicationUser> userManager, ApplicationDbContext db)
{
    public async Task<StaffDataScope?> GetAsync(ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null || !user.IsActive) return null;

        var isAdministrator = await userManager.IsInRoleAsync(user, SystemRoles.SuperAdministrator) ||
                              await userManager.IsInRoleAsync(user, SystemRoles.Administrator);
        var now = DateTimeOffset.UtcNow;
        var scopes = await db.UserAccessScopes.AsNoTracking().Where(x => x.UserId == user.Id && x.IsActive && x.EffectiveFrom <= now && (x.ExpiresAt == null || x.ExpiresAt > now)).ToListAsync();
        var divisions = scopes.Where(x => x.DivisionId.HasValue).Select(x => x.DivisionId!.Value).ToHashSet();
        var branches = scopes.Where(x => x.BranchId.HasValue).Select(x => x.BranchId!.Value).ToHashSet();
        if (user.DivisionId.HasValue) divisions.Add(user.DivisionId.Value);
        if (user.BranchId.HasValue) branches.Add(user.BranchId.Value);
        var groupWide = isAdministrator || scopes.Any(x => x.Type == AccessScopeType.GroupWide);
        return new StaffDataScope(groupWide, user.DivisionId, user.BranchId, user.Id, user.FullName, divisions, branches);
    }
}

public sealed record StaffDataScope(bool IsAdministrator, Guid? DivisionId, Guid? BranchId, Guid UserId, string UserName, IReadOnlySet<Guid> DivisionIds, IReadOnlySet<Guid> BranchIds)
{
    public bool HasBranchAccess(Guid branchId) => IsAdministrator || BranchIds.Contains(branchId);
    public bool HasDivisionAccess(Guid? divisionId) => IsAdministrator || divisionId.HasValue && DivisionIds.Contains(divisionId.Value);
    public bool HasAssetAccess(Guid branchId, Guid? divisionId) => HasBranchAccess(branchId) && HasDivisionAccess(divisionId);
}
