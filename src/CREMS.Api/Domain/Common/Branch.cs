namespace CREMS.Api.Domain.Common;

public sealed class Branch : Entity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}

