using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController, Route("api/staff-roles"), Authorize(Policy = SystemPolicies.ManageUsers)]
public sealed class StaffRolesController(ApplicationDbContext db, RoleManager<IdentityRole<Guid>> roles, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get(CancellationToken token)
    {
        var all = await db.Roles.AsNoTracking().Where(role => role.Name != SystemRoles.Customer && (SystemRoles.Staff.Contains(role.Name!) || db.RolePermissions.Any(grant => grant.RoleName == role.Name && grant.Permission.StartsWith("staff.module.")))).OrderBy(role => role.Name).ToListAsync(token);
        var grants = await db.RolePermissions.AsNoTracking().ToListAsync(token);
        return Ok(new {
            modules = StaffRoleAccess.Modules,
            permissions = StaffRoleAccess.OperationalPermissions,
            builtInPermissions = SystemPermissions.All,
            roles = all.Select(role => new {
                name = role.Name!, isBuiltIn = SystemRoles.Staff.Contains(role.Name!),
                modules = SystemRoles.Staff.Contains(role.Name!) ? BuiltInModules(role.Name!) :
                    StaffRoleAccess.Modules.Where(module => grants.Any(grant => grant.RoleName == role.Name && grant.Permission == StaffRoleAccess.Grant(module.Key))).Select(module => module.Key).ToArray(),
                permissions = role.Name == SystemRoles.SuperAdministrator ? SystemPermissions.All : grants.Where(grant => grant.RoleName == role.Name && SystemPermissions.All.Contains(grant.Permission)).Select(grant => grant.Permission).ToArray()
            })
        });
    }

    [HttpPost, Authorize(Roles = SystemRoles.SuperAdministrator)]
    public async Task<ActionResult> Create(StaffRoleRequest request, CancellationToken token)
    {
        var name = request.Name.Trim();
        if (name.Length < 2 || name.Length > 80 || name.Any(character => !char.IsLetterOrDigit(character) && character != ' ' && character != '-' && character != '&'))
            return BadRequest(new { message = "Use 2–80 letters, numbers, spaces, hyphens or & for the role name." });
        if (await roles.RoleExistsAsync(name)) return Conflict(new { message = "A role with this name already exists." });
        if (!Valid(request)) return BadRequest(new { message = "Select at least one existing module and use only the listed permissions." });
        var role = new IdentityRole<Guid>(name) { Id = Guid.NewGuid() };
        var result = await roles.CreateAsync(role);
        if (!result.Succeeded) return BadRequest(new { message = string.Join(" ", result.Errors.Select(error => error.Description)) });
        await Save(name, request, token);
        return Ok(new { name });
    }

    [HttpPut("{name}"), Authorize(Roles = SystemRoles.SuperAdministrator)]
    public async Task<ActionResult> Update(string name, StaffRoleRequest request, CancellationToken token)
    {
        var role = await roles.FindByNameAsync(name);
        var canonicalName = role?.Name ?? name;
        if (canonicalName == SystemRoles.SuperAdministrator || canonicalName == SystemRoles.Customer) return BadRequest(new { message = "The system owner and customer roles cannot be changed here." });
        if (role is null) return NotFound();
        if (SystemRoles.Staff.Contains(canonicalName))
        {
            var allowed = canonicalName == SystemRoles.Administrator ? SystemPermissions.All : SystemPermissions.All.Except([SystemPermissions.UsersCreate, SystemPermissions.UsersResetPassword, SystemPermissions.UsersManageAccess, SystemPermissions.DivisionsConfigure]).ToArray();
            if (request.Permissions.Any(permission => !allowed.Contains(permission))) return BadRequest(new { message = "Select only the available permissions for this role." });
            db.RolePermissions.RemoveRange(await db.RolePermissions.Where(grant => grant.RoleName == canonicalName).ToListAsync(token));
            foreach (var permission in request.Permissions.Distinct().Append("staff.configured"))
                db.RolePermissions.Add(new RolePermission { RoleName = canonicalName, Permission = permission });
            var scope = await staffScope.GetAsync(User);
            if (scope is not null) AuditWriter.Record(db, scope, "Staff role configured", "Role", role.Id, $"{canonicalName}: action permissions updated.", null);
            await db.SaveChangesAsync(token);
            return NoContent();
        }
        if (!Valid(request)) return BadRequest(new { message = "Select at least one existing module and use only the listed permissions." });
        await Save(canonicalName, request, token);
        return NoContent();
    }
    private static bool Valid(StaffRoleRequest request) => request.Modules.Length > 0 &&
        request.Modules.All(key => StaffRoleAccess.Modules.Any(module => module.Key == key)) &&
        request.Permissions.All(StaffRoleAccess.OperationalPermissions.Contains);
    private async Task Save(string name, StaffRoleRequest request, CancellationToken token)
    {
        db.RolePermissions.RemoveRange(await db.RolePermissions.Where(grant => grant.RoleName == name).ToListAsync(token));
        foreach (var permission in request.Modules.Select(StaffRoleAccess.Grant).Concat(request.Permissions).Distinct())
            db.RolePermissions.Add(new RolePermission { RoleName = name, Permission = permission });
        var scope = await staffScope.GetAsync(User);
        if (scope is not null) AuditWriter.Record(db, scope, "Staff role configured", "Role", scope.UserId,
            $"{name}: {string.Join(", ", request.Modules)}.", null);
        await db.SaveChangesAsync(token);
    }
    private static string[] BuiltInModules(string role) => role switch {
        SystemRoles.SuperAdministrator or SystemRoles.Administrator or SystemRoles.BranchManager => StaffRoleAccess.Modules.Select(module => module.Key).ToArray(),
        SystemRoles.RentalOfficer => [SystemPolicies.ViewAssets, SystemPolicies.ManageRentals, SystemPolicies.UseAssetQr],
        SystemRoles.MaintenanceOfficer => [SystemPolicies.ViewAssets, SystemPolicies.ManageMaintenance, SystemPolicies.UseAssetQr],
        _ => []
    };
}
public sealed record StaffRoleRequest([Required] string Name, [Required] string[] Modules, [Required] string[] Permissions);

