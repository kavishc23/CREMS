namespace CREMS.Api.Domain.Identity;

public static class SystemRoles
{
    public const string SuperAdministrator = "SuperAdministrator";
    public const string Administrator = "Administrator";
    public const string BranchManager = "BranchManager";
    public const string RentalOfficer = "RentalOfficer";
    public const string MaintenanceOfficer = "MaintenanceOfficer";
    public const string Customer = "Customer";
    public static readonly string[] All =
    [
        SuperAdministrator,
        Administrator,
        BranchManager,
        RentalOfficer,
        MaintenanceOfficer,
        Customer
    ];
    public static readonly string[] Staff =
    [
        SuperAdministrator,
        Administrator,
        BranchManager,
        RentalOfficer,
        MaintenanceOfficer
    ];

    public static readonly string[] GroupWide = [SuperAdministrator, Administrator];
    public static readonly string[] BranchScoped =
        [BranchManager, RentalOfficer, MaintenanceOfficer];
}
