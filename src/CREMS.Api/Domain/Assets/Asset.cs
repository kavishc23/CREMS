using CREMS.Api.Domain.Common;

namespace CREMS.Api.Domain.Assets;

public sealed class Asset : Entity
{
    public required string AssetNumber { get; set; }
    public required string Name { get; set; }
    public AssetType Type { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.Available;
    public Guid BranchId { get; set; }
    public Branch? Branch { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? SerialNumber { get; set; }
    public decimal DailyRate { get; set; }
    public DateOnly? NextServiceDate { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum AssetType
{
    Vehicle,
    Equipment
}

public enum AssetStatus
{
    Available,
    Reserved,
    Rented,
    Inspection,
    Maintenance,
    OutOfService,
    Retired
}

