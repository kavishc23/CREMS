namespace CREMS.Api.Domain.Identity;

public static class SystemRoles
{
    public const string Administrator = "Administrator";
    public const string RentalOfficer = "RentalOfficer";
    public const string BranchManager = "BranchManager";
    public static readonly string[] All =
    [
        Administrator,
        BranchManager,
        RentalOfficer
    ];
}
