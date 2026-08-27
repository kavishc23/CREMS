using System.Security.Claims;
using CREMS.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Domain.Identity;

public sealed class CurrentStaffScope(ApplicationDbContext db)
{
    public async Task<StaffDataScope?> GetAsync(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(id, out var userId)) return null;
        var user = await db.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.Id, x.IsActive, x.DivisionId, x.BranchId, x.FullName })
            .SingleOrDefaultAsync();
        if (user is null || !user.IsActive) return null;

        // Identity already validated and placed roles in the authenticated cookie.
        // Reading those claims avoids two AspNetUserRoles queries on every API call.
        var isAdministrator = principal.IsInRole(SystemRoles.SuperAdministrator) ||
                              principal.IsInRole(SystemRoles.Administrator);
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
