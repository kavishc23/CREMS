namespace CREMS.Api.Domain.Common;

public sealed class Branch : Entity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? PostalAddress { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public Guid? BranchManagerUserId { get; set; }
    public string? PickupInstructions { get; set; }
    public string? ReturnInstructions { get; set; }
    public string? DeliveryCoverage { get; set; }
    public bool IsPublic { get; set; } = true;
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
    public DateOnly? OpenedOn { get; set; }
    public string? LocalContactEmail { get; set; }
    public string? LocalContactPhone { get; set; }
    public Guid? DivisionManagerUserId { get; set; }
    public bool AcceptsBookings { get; set; } = true;
    public bool HasMaintenanceCapability { get; set; }
    public Guid? DefaultApprovalWorkflowId { get; set; }
    public string LocalTermsJson { get; set; } = "{}";
    public string PricingOverridesJson { get; set; } = "{}";
    public ICollection<BranchDivisionService> Services { get; set; } = [];
}

public sealed class BranchDivisionService : Entity
{
    public Guid BranchId { get; set; }
    public Guid DivisionId { get; set; }
    public Guid ServiceOfferingId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsBookable { get; set; } = true;
}

public sealed class BranchOperatingPeriod : Entity
{
    public Guid BranchId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly OpensAt { get; set; }
    public TimeOnly ClosesAt { get; set; }
    public TimeOnly? PickupCutoff { get; set; }
    public TimeOnly? ReturnCutoff { get; set; }
    public bool IsClosed { get; set; }
    public decimal AfterHoursCharge { get; set; }
}

public sealed class BranchCalendarException : Entity
{
    public Guid BranchId { get; set; }
    public DateOnly Date { get; set; }
    public required string Name { get; set; }
    public bool IsClosed { get; set; }
    public TimeOnly? OpensAt { get; set; }
    public TimeOnly? ClosesAt { get; set; }
    public decimal AfterHoursCharge { get; set; }
}
