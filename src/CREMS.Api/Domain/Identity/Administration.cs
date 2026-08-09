using CREMS.Api.Domain.Common;

namespace CREMS.Api.Domain.Identity;

public sealed class SystemSetting : Entity
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public required string Category { get; set; }
    public string? Description { get; set; }
    public bool IsSecret { get; set; }
}

public sealed class NotificationTemplate : Entity
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Channel { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
    public bool IsActive { get; set; } = true;
}
