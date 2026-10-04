using System.Security.Claims;
using System.Reflection;
using CREMS.Api.Controllers;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class SwaggerAuthorizationTests
{
    [Theory]
    [InlineData(SystemPolicies.StaffPortal)]
    [InlineData(SystemPolicies.ManageMaintenance)]
    [InlineData(SystemPolicies.ManageRentals)]
    [InlineData(SystemPolicies.ManageUsers)]
    [InlineData(SystemPolicies.ViewReports)]
    [InlineData(SystemPolicies.AdministerSystem)]
    public async Task Customer_cannot_pass_staff_action_policies(string policy)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationAuthorization();
        await using var provider = services.BuildServiceProvider();
        var auth = provider.GetRequiredService<IAuthorizationService>();
        var customer = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, SystemRoles.Customer)], "test"));
        Assert.False((await auth.AuthorizeAsync(customer, null, policy)).Succeeded);
    }

    [Theory]
    [InlineData(typeof(MaintenanceJobsController))]
    [InlineData(typeof(BookingsController))]
    [InlineData(typeof(CustomersController))]
    public async Task Staff_controllers_keep_authorization_that_denies_customers(Type controller)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddApplicationAuthorization();
        await using var provider = services.BuildServiceProvider();
        var rules = controller.GetCustomAttributes<AuthorizeAttribute>().ToArray();
        Assert.NotEmpty(rules);
        Assert.Empty(controller.GetCustomAttributes<AllowAnonymousAttribute>());
        var policy = await AuthorizationPolicy.CombineAsync(provider.GetRequiredService<IAuthorizationPolicyProvider>(), rules);
        Assert.NotNull(policy);
        var customer = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, SystemRoles.Customer)], "test"));
        Assert.False((await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(customer, null, policy)).Succeeded);
    }

    [Fact]
    public async Task Maintenance_policy_accepts_officer_but_rejects_rental_officer()
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddApplicationAuthorization();
        await using var provider = services.BuildServiceProvider();
        var auth = provider.GetRequiredService<IAuthorizationService>();
        foreach (var role in new[] { SystemRoles.MaintenanceOfficer, SystemRoles.RentalOfficer })
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));
            Assert.Equal(role == SystemRoles.MaintenanceOfficer, (await auth.AuthorizeAsync(user, null, SystemPolicies.ManageMaintenance)).Succeeded);
        }
    }
}
