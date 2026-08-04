namespace CREMS.Api.Domain.Common;

public sealed class AuditEvent : Entity
{
    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public Guid UserId { get; set; }
    public required string UserName { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public required string Summary { get; set; }
    public string? PreviousValues { get; set; }
    public string? NewValues { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
