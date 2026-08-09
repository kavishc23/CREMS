namespace CREMS.Api.Domain.Common;

public sealed class Branch : Entity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<BranchDivision> Divisions { get; set; } = [];
}

public sealed class BranchDivision
{
    public Guid BranchId { get; set; }
    public Branch? Branch { get; set; }
    public Guid DivisionId { get; set; }
    public Division? Division { get; set; }
    public bool IsActive { get; set; } = true;
}
