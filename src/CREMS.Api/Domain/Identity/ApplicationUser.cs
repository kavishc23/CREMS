using CREMS.Api.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace CREMS.Api.Domain.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;
    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public bool IsActive { get; set; } = true;
}
