using CREMS.Api.Domain.Common;

namespace CREMS.Api.Domain.Customers;

public sealed class CustomerLicence : Entity
{
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public required string StorageKey { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string ContentHash { get; set; }
    public string? ExtractedName { get; set; }
    public string? LicenceNumber { get; set; }
    public string LicenceClasses { get; set; } = string.Empty;
    public LicenceVerificationStatus Status { get; set; } = LicenceVerificationStatus.Required;
    public LicenceOcrStatus OcrStatus { get; set; } = LicenceOcrStatus.NotRun;
    public string? OcrFailureCode { get; set; }
    public LicenceUploadSource UploadSource { get; set; }
    public Guid? UploadedByUserId { get; set; }
    public Guid? ConfirmedByUserId { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
}

public enum LicenceVerificationStatus { Required, Verified }
public enum LicenceOcrStatus { NotRun, Completed, Failed }
public enum LicenceUploadSource { Customer, Administrator }
