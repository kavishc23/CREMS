using CREMS.Api.Data;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = SystemPolicies.AdministerSystem)]
public sealed class UsersController(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var users = await userManager.Users
            .AsNoTracking()
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.Email)
            .ToListAsync(cancellationToken);

        var response = new List<UserResponse>(users.Count);
        foreach (var user in users)
        {
            response.Add(new UserResponse(
                user.Id,
                user.Email ?? string.Empty,
                user.FullName,
                user.BranchId,
                user.IsActive,
                (await userManager.GetRolesAsync(user)).ToArray()));
        }

        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request)
    {
        var role = SystemRoles.All.FirstOrDefault(
            candidate => string.Equals(candidate, request.Role, StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            ModelState.AddModelError(nameof(request.Role), "Select a valid CREMS role.");
            return ValidationProblem(ModelState);
        }

        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError(nameof(request.Email), "An account with this email already exists.");
            return ValidationProblem(ModelState);
        }

        if (request.BranchId.HasValue &&
            !await db.Branches.AnyAsync(branch => branch.Id == request.BranchId.Value))
        {
            ModelState.AddModelError(nameof(request.BranchId), "The selected branch does not exist.");
            return ValidationProblem(ModelState);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = request.FullName.Trim(),
            BranchId = request.BranchId,
            IsActive = true,
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return ValidationProblem(ModelState);
        }

        var roleResult = await userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            foreach (var error in roleResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return ValidationProblem(ModelState);
        }

        return CreatedAtAction(nameof(GetAll), new UserResponse(
            user.Id,
            user.Email,
            user.FullName,
            user.BranchId,
            user.IsActive,
            [role]));
    }
}

public sealed record CreateUserRequest(
    string FullName,
    string Email,
    string Password,
    string Role,
    Guid? BranchId);

public sealed record UserResponse(
    Guid Id,
    string Email,
    string FullName,
    Guid? BranchId,
    bool IsActive,
    IReadOnlyCollection<string> Roles);
