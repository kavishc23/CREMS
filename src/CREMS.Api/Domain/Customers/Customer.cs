using CREMS.Api.Domain.Common;

namespace CREMS.Api.Domain.Customers;

public sealed class Customer : Entity
{
    public required string CustomerNumber { get; set; }
    public CustomerType Type { get; set; }
    public required string Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? IdentificationNumber { get; set; }
    public List<CustomerHirePreference> HirePreferences { get; set; } = [];
    public bool IsBlocked { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum CustomerHirePreference
{
    Vehicles,
    Equipment,
    WasteAndSiteHire,
}

public enum CustomerType
{
    Individual,
    Business
}
