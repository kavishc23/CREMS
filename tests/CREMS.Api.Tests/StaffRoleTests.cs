using System.Security.Claims;
using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class StaffRoleTests
{
    [Fact]
    public async Task Custom_role_can_be_assigned_and_only_opens_selected_modules()
    {
        await using var f = await Fixture.Create();
        var controller = new StaffRolesController(f.Db, f.Services.GetRequiredService<RoleManager<IdentityRole<Guid>>>(), new CurrentStaffScope(f.Db)) { ControllerContext = new() { HttpContext = f.Context } };
        Assert.IsType<OkObjectResult>(await controller.Create(new StaffRoleRequest("Workshop coordinator", [SystemPolicies.ViewAssets, SystemPolicies.ManageMaintenance], [SystemPermissions.MaintenanceComplete, SystemPermissions.AssetsEdit]), TestContext.Current.CancellationToken));
        var users = new UsersController(f.Users, f.Db, new CurrentStaffScope(f.Db)) { ControllerContext = new() { HttpContext = f.Context } };
        var created = await users.Create(new CreateUserRequest("Workshop staff", "workshop@example.test", "Testing12345!", "Workshop coordinator", f.Division.Id, f.Branch.Id, null));
        var response = Assert.IsType<UserResponse>(Assert.IsType<CreatedAtActionResult>(created.Result).Value);
        Assert.Contains("Workshop coordinator", response.Roles);
        var user = await f.Users.FindByEmailAsync("workshop@example.test");
        Assert.NotNull(user);
        var principal = await f.Services.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(user);
        var authorization = f.Services.GetRequiredService<IAuthorizationService>();
        Assert.True((await authorization.AuthorizeAsync(principal, SystemPolicies.StaffPortal)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(principal, SystemPolicies.ViewAssets)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(principal, SystemPolicies.ManageMaintenance)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(principal, SystemPermissions.MaintenanceComplete)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(principal, "ManageAssetPhotos")).Succeeded);
        f.Db.UserPermissionOverrides.Add(new UserPermissionOverride { UserId = user.Id, Permission = SystemPermissions.AssetsEdit, IsGranted = false });
        await f.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.False((await authorization.AuthorizeAsync(principal, "ManageAssetPhotos")).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(principal, SystemPolicies.ManageRentals)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(principal, SystemPolicies.ManageUsers)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(principal, SystemPolicies.AdministerSystem)).Succeeded);
        var listed = await users.GetAll(TestContext.Current.CancellationToken);
        Assert.Contains(Assert.IsAssignableFrom<IReadOnlyList<UserResponse>>(Assert.IsType<OkObjectResult>(listed.Result).Value), item => item.Id == user.Id);
        var notifications = new NotificationsController(f.Db, f.Users, new CurrentStaffScope(f.Db), authorization) { ControllerContext = new() { HttpContext = f.Context } };
        var targets = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await notifications.Targets(TestContext.Current.CancellationToken)).Value);
        Assert.Contains(targets.GetProperty("roles").EnumerateArray(), item => item.GetString() == "Workshop coordinator");
        Assert.Contains(targets.GetProperty("staff").EnumerateArray(), item => item.GetProperty("Id").GetGuid() == user.Id);
    }

    [Fact]
    public async Task Role_configuration_rejects_unknown_modules_and_protected_permissions()
    {
        await using var f = await Fixture.Create();
        var controller = new StaffRolesController(f.Db, f.Services.GetRequiredService<RoleManager<IdentityRole<Guid>>>(), new CurrentStaffScope(f.Db)) { ControllerContext = new() { HttpContext = f.Context } };
        Assert.IsType<BadRequestObjectResult>(await controller.Create(new StaffRoleRequest("Unknown page", ["NewPage"], []), TestContext.Current.CancellationToken));
        Assert.IsType<BadRequestObjectResult>(await controller.Create(new StaffRoleRequest("Escalation", [SystemPolicies.ViewAssets], [SystemPermissions.UsersManageAccess]), TestContext.Current.CancellationToken));
        Assert.IsType<BadRequestObjectResult>(await controller.Update(SystemRoles.SuperAdministrator, new StaffRoleRequest(SystemRoles.SuperAdministrator, [SystemPolicies.ViewAssets], []), TestContext.Current.CancellationToken));
        Assert.Empty(await f.Db.RolePermissions.ToListAsync(TestContext.Current.CancellationToken));
        var protectedCreation = typeof(StaffRolesController).GetMethod(nameof(StaffRolesController.Create))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>();
        Assert.Contains(protectedCreation, attribute => attribute.Roles == SystemRoles.SuperAdministrator);
    }

    [Fact]
    public async Task Existing_operational_role_permissions_can_be_edited_and_survive_default_seeding()
    {
        await using var f = await Fixture.Create();
        var manager = f.Services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        Assert.True((await manager.CreateAsync(new IdentityRole<Guid>(SystemRoles.RentalOfficer))).Succeeded);
        f.Db.RolePermissions.Add(new RolePermission { RoleName = SystemRoles.RentalOfficer, Permission = SystemPermissions.CustomersManageAccess });
        await f.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = new StaffRolesController(f.Db, manager, new CurrentStaffScope(f.Db)) { ControllerContext = new() { HttpContext = f.Context } };
        Assert.IsType<NoContentResult>(await controller.Update("rentalofficer", new StaffRoleRequest(SystemRoles.RentalOfficer, [], [SystemPermissions.AssetsInspect]), TestContext.Current.CancellationToken));
        Assert.False(await f.Db.RolePermissions.AnyAsync(grant => grant.RoleName == SystemRoles.RentalOfficer && grant.Permission == SystemPermissions.CustomersManageAccess, TestContext.Current.CancellationToken));
        var seed = typeof(DatabaseInitializer).GetMethod("SeedRolePermissionsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        await (Task)seed.Invoke(null, [f.Db, TestContext.Current.CancellationToken])!;
        var saved = await f.Db.RolePermissions.Where(grant => grant.RoleName == SystemRoles.RentalOfficer).Select(grant => grant.Permission).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(SystemPermissions.AssetsInspect, saved);
        Assert.DoesNotContain(SystemPermissions.CustomersManageAccess, saved);
        Assert.IsType<BadRequestObjectResult>(await controller.Update(SystemRoles.RentalOfficer, new StaffRoleRequest(SystemRoles.RentalOfficer, [], [SystemPermissions.UsersManageAccess]), TestContext.Current.CancellationToken));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ServiceProvider Services { get; }
        public ApplicationDbContext Db => Services.GetRequiredService<ApplicationDbContext>();
        public UserManager<ApplicationUser> Users => Services.GetRequiredService<UserManager<ApplicationUser>>();
        public DefaultHttpContext Context { get; } = new();
        public Branch Branch { get; } = new() { Code = "TEST", Name = "Test branch" };
        public Division Division { get; } = new() { Code = "TEST", Name = "Test division" };
        private Fixture(ServiceProvider services) { Services = services; Context.RequestServices = services; }
        public static async Task<Fixture> Create()
        {
            var services = new ServiceCollection();
            services.AddLogging(); services.AddMvcCore();
            services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase("staff-role-" + Guid.NewGuid()));
            services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<ApplicationDbContext>();
            services.AddApplicationAuthorization();
            services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
            var f = new Fixture(services.BuildServiceProvider());
            var administrator = new ApplicationUser { Id = Guid.NewGuid(), Email = "admin@example.test", UserName = "admin@example.test", FullName = "Test administrator", IsActive = true };
            f.Db.AddRange(administrator, f.Branch, f.Division, new BranchDivision { BranchId = f.Branch.Id, DivisionId = f.Division.Id });
            using (f.Db.SuppressNotifications()) await f.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
            f.Context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, administrator.Id.ToString()), new Claim(ClaimTypes.Role, SystemRoles.SuperAdministrator)], "test"));
            return f;
        }
        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }
}

