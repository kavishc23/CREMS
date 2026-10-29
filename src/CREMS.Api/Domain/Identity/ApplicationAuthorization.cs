using Microsoft.AspNetCore.Identity;

namespace CREMS.Api.Domain.Identity;

public static class ApplicationAuthorization
{
    public static void AddApplicationAuthorization(this IServiceCollection services)
    {
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, StaffPolicyAuthorizationHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(SystemPolicies.StaffPortal, policy =>
                policy.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme).RequireAuthenticatedUser().AddRequirements(new StaffPolicyRequirement(SystemPolicies.StaffPortal, SystemRoles.Staff)))
            .AddPolicy(SystemPolicies.AdministerSystem, policy =>
                policy.RequireRole(SystemRoles.SuperAdministrator))
            .AddPolicy(SystemPolicies.ManageUsers, policy =>
                policy.RequireRole(SystemRoles.SuperAdministrator, SystemRoles.Administrator))
            .AddPolicy(SystemPolicies.ManageDivisions, policy =>
                policy.RequireRole(SystemRoles.SuperAdministrator))
            .AddPolicy(SystemPolicies.ManageBranch, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new StaffPolicyRequirement(SystemPolicies.ManageBranch, [SystemRoles.SuperAdministrator, SystemRoles.Administrator, SystemRoles.BranchManager])))
            .AddPolicy(SystemPolicies.ManageRentals, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new StaffPolicyRequirement(SystemPolicies.ManageRentals, [SystemRoles.SuperAdministrator,
                    SystemRoles.Administrator,
                    SystemRoles.BranchManager,
                    SystemRoles.RentalOfficer])))
            .AddPolicy(SystemPolicies.ViewAssets, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new StaffPolicyRequirement(SystemPolicies.ViewAssets, [SystemRoles.SuperAdministrator,
                    SystemRoles.Administrator,
                    SystemRoles.BranchManager,
                    SystemRoles.RentalOfficer,
                    SystemRoles.MaintenanceOfficer])))
            .AddPolicy(SystemPolicies.ManageMaintenance, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new StaffPolicyRequirement(SystemPolicies.ManageMaintenance, [SystemRoles.SuperAdministrator, SystemRoles.Administrator, SystemRoles.BranchManager, SystemRoles.MaintenanceOfficer])))
            .AddPolicy(SystemPolicies.ManageFinance, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new StaffPolicyRequirement(SystemPolicies.ManageFinance, [SystemRoles.SuperAdministrator, SystemRoles.Administrator, SystemRoles.BranchManager])))
            .AddPolicy(SystemPolicies.UseAssetQr, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new StaffPolicyRequirement(SystemPolicies.UseAssetQr, [SystemRoles.SuperAdministrator, SystemRoles.Administrator, SystemRoles.BranchManager, SystemRoles.RentalOfficer, SystemRoles.MaintenanceOfficer])))
            .AddPolicy(SystemPolicies.ViewReports, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new StaffPolicyRequirement(SystemPolicies.ViewReports, [SystemRoles.SuperAdministrator, SystemRoles.Administrator, SystemRoles.BranchManager])))
            .AddPolicy("ManageAssetPhotos", policy => policy.RequireAuthenticatedUser().AddRequirements(new StaffPolicyRequirement(SystemPermissions.AssetsEdit, [SystemRoles.SuperAdministrator, SystemRoles.Administrator, SystemRoles.BranchManager, SystemRoles.RentalOfficer])))
            .AddPolicy(SystemPolicies.CustomerPortal, policy =>
                policy.AddAuthenticationSchemes(SystemAuthenticationSchemes.Customer).RequireRole(SystemRoles.Customer))
            .AddPolicy(SystemPermissions.UsersCreate, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.UsersCreate)))
            .AddPolicy(SystemPermissions.UsersResetPassword, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.UsersResetPassword)))
            .AddPolicy(SystemPermissions.UsersManageAccess, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.UsersManageAccess)))
            .AddPolicy(SystemPermissions.CustomersManageAccess, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.CustomersManageAccess)))
            .AddPolicy(SystemPermissions.RentalsApprove, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.RentalsApprove)))
            .AddPolicy(SystemPermissions.MaintenanceComplete, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.MaintenanceComplete)))
            .AddPolicy(SystemPermissions.PaymentsRefund, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.PaymentsRefund)))
            .AddPolicy(SystemPermissions.ReportsFinancial, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.ReportsFinancial)))
            .AddPolicy(SystemPermissions.DivisionsConfigure, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.DivisionsConfigure)))
            .AddPolicy(SystemPermissions.BranchesConfigure, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.BranchesConfigure)))
            .AddPolicy(SystemPermissions.ServicesConfigure, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.ServicesConfigure)))
            .AddPolicy(SystemPermissions.AssetCategoriesConfigure, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetCategoriesConfigure)))
            .AddPolicy(SystemPermissions.BranchCalendarManage, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.BranchCalendarManage)))
            .AddPolicy(SystemPermissions.PricingConfigure, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.PricingConfigure)))
            .AddPolicy(SystemPermissions.AssetsView, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetsView)))
            .AddPolicy(SystemPermissions.AssetsCreate, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetsCreate)))
            .AddPolicy(SystemPermissions.AssetsEdit, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetsEdit)))
            .AddPolicy(SystemPermissions.AssetsTransfer, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetsTransfer)))
            .AddPolicy(SystemPermissions.AssetsInspect, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetsInspect)))
            .AddPolicy(SystemPermissions.AssetsRecordMeter, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetsRecordMeter)))
            .AddPolicy(SystemPermissions.AssetsRetire, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetsRetire)))
            .AddPolicy(SystemPermissions.AssetsViewFinancials, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetsViewFinancials)));
    }
}
