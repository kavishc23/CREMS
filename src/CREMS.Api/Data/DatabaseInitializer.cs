using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        await db.Database.MigrateAsync(cancellationToken);

        foreach (var roleName in new[]
                 {
                     SystemRoles.Administrator,
                     SystemRoles.BranchManager,
                     SystemRoles.RentalOfficer,
                 })
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
                EnsureSucceeded(roleResult, $"create the {roleName} role");
            }
        }

        var email = app.Configuration["BootstrapAdmin:Email"];
        var password = app.Configuration["BootstrapAdmin:Password"];
        var fullName = app.Configuration["BootstrapAdmin:FullName"] ?? "System Administrator";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            app.Logger.LogWarning(
                "No bootstrap administrator was configured. Set BootstrapAdmin credentials using user secrets before first use.");
            return;
        }

        var administrator = await userManager.FindByEmailAsync(email);
        if (administrator is null)
        {
            administrator = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                IsActive = true,
            };

            var createResult = await userManager.CreateAsync(administrator, password);
            EnsureSucceeded(createResult, "create the bootstrap administrator");
        }

        if (!await userManager.IsInRoleAsync(administrator, SystemRoles.Administrator))
        {
            var roleResult = await userManager.AddToRoleAsync(administrator, SystemRoles.Administrator);
            EnsureSucceeded(roleResult, "assign the Administrator role");
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded) return;

        var errors = string.Join("; ", result.Errors.Select(error => error.Description));
        throw new InvalidOperationException($"Unable to {operation}: {errors}");
    }
}

