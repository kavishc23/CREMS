using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace CREMS.Api.Domain.Identity;

public sealed class CurrentStaffScope(UserManager<ApplicationUser> userManager)
{
    public async Task<StaffDataScope?> GetAsync(ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null || !user.IsActive) return null;

        var isAdministrator = await userManager.IsInRoleAsync(user, SystemRoles.Administrator);
        return new StaffDataScope(isAdministrator, user.DivisionId, user.BranchId, user.Id, user.FullName);
    }
}

public sealed record StaffDataScope(bool IsAdministrator, Guid? DivisionId, Guid? BranchId, Guid UserId, string UserName)
{
    public bool HasBranchAccess(Guid branchId) => IsAdministrator || BranchId == branchId;
    public bool HasDivisionAccess(Guid? divisionId) => IsAdministrator || DivisionId.HasValue && DivisionId == divisionId;
    public bool HasAssetAccess(Guid branchId, Guid? divisionId) => HasBranchAccess(branchId) && HasDivisionAccess(divisionId);
}
