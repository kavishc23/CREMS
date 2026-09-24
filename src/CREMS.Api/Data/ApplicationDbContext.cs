using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Domain.Operations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CREMS.Api.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<InAppNotification> InAppNotifications => Set<InAppNotification>();
    public DbSet<NotificationRead> NotificationReads => Set<NotificationRead>();
    public DbSet<StaffNotificationPreference> StaffNotificationPreferences => Set<StaffNotificationPreference>();
    public DbSet<NotificationEmailDelivery> NotificationEmailDeliveries => Set<NotificationEmailDelivery>();
    private bool notificationsSuppressed;
    public IDisposable SuppressNotifications()
    {
        var previous = notificationsSuppressed;
        notificationsSuppressed = true;
        return new NotificationSuppression(this, previous);
    }
    private sealed class NotificationSuppression(ApplicationDbContext context, bool previous) : IDisposable
    {
        public void Dispose() => context.notificationsSuppressed = previous;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (!notificationsSuppressed) await CREMS.Api.Services.NotificationEvents.CaptureAsync(this, cancellationToken);
        var notificationChanged = ChangeTracker.Entries<InAppNotification>().Any(x => x.State is EntityState.Added or EntityState.Modified)
            || ChangeTracker.Entries<NotificationRead>().Any(x => x.State == EntityState.Added);
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (notificationChanged && !notificationsSuppressed) CREMS.Api.Services.NotificationWakeup.Publish();
        return result;
    }
    private static readonly JsonSerializerOptions HirePreferenceJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Division> Divisions => Set<Division>();
    public DbSet<ServiceOffering> ServiceOfferings => Set<ServiceOffering>();
    public DbSet<ChargeDefinition> ChargeDefinitions => Set<ChargeDefinition>();
    public DbSet<BranchDivision> BranchDivisions => Set<BranchDivision>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetCostEntry> AssetCostEntries => Set<AssetCostEntry>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingItem> BookingItems => Set<BookingItem>();
    public DbSet<BookingCharge> BookingCharges => Set<BookingCharge>();
    public DbSet<RentalInspection> RentalInspections => Set<RentalInspection>();
    public DbSet<RentalAgreement> RentalAgreements => Set<RentalAgreement>();
    public DbSet<AuthorizedDriver> AuthorizedDrivers => Set<AuthorizedDriver>();
    public DbSet<RentalPayment> RentalPayments => Set<RentalPayment>();
    public DbSet<RentalNotification> RentalNotifications => Set<RentalNotification>();
    public DbSet<RentalIncident> RentalIncidents => Set<RentalIncident>();
    public DbSet<RentalInvoice> RentalInvoices => Set<RentalInvoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<RentalAgreementAddendum> RentalAgreementAddendums => Set<RentalAgreementAddendum>();
    public DbSet<MaintenanceJob> MaintenanceJobs => Set<MaintenanceJob>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<SalesQuote> SalesQuotes => Set<SalesQuote>();
    public DbSet<CorporateAccount> CorporateAccounts => Set<CorporateAccount>();
    public DbSet<DispatchJob> DispatchJobs => Set<DispatchJob>();
    public DbSet<AssetTransfer> AssetTransfers => Set<AssetTransfer>();
    public DbSet<PricingRule> PricingRules => Set<PricingRule>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<ApprovalWorkflow> ApprovalWorkflows => Set<ApprovalWorkflow>();
    public DbSet<ApprovalWorkflowStage> ApprovalWorkflowStages => Set<ApprovalWorkflowStage>();
    public DbSet<ApprovalStageDecision> ApprovalStageDecisions => Set<ApprovalStageDecision>();
    public DbSet<InventoryPart> InventoryParts => Set<InventoryPart>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<TelematicsSnapshot> TelematicsSnapshots => Set<TelematicsSnapshot>();
    public DbSet<CustomerCase> CustomerCases => Set<CustomerCase>();
    public DbSet<DocumentRecord> DocumentRecords => Set<DocumentRecord>();
    public DbSet<ManagementTask> ManagementTasks => Set<ManagementTask>();
    public DbSet<OutboundEmail> OutboundEmails => Set<OutboundEmail>();
    public DbSet<PasswordResetOtp> PasswordResetOtps => Set<PasswordResetOtp>();
    public DbSet<EmailVerificationOtp> EmailVerificationOtps => Set<EmailVerificationOtp>();
    public DbSet<CustomerAccountActivation> CustomerAccountActivations => Set<CustomerAccountActivation>();
    public DbSet<AssetLifecycleEvent> AssetLifecycleEvents => Set<AssetLifecycleEvent>();
    public DbSet<AssetMeterReading> AssetMeterReadings => Set<AssetMeterReading>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<MaintenancePartUsage> MaintenancePartUsages => Set<MaintenancePartUsage>();
    public DbSet<Personnel> Personnel => Set<Personnel>();
    public DbSet<PersonnelQualification> PersonnelQualifications => Set<PersonnelQualification>();
    public DbSet<BookingPersonnelAssignment> BookingPersonnelAssignments => Set<BookingPersonnelAssignment>();
    public DbSet<PersonnelTimesheet> PersonnelTimesheets => Set<PersonnelTimesheet>();
    public DbSet<DeliveryZone> DeliveryZones => Set<DeliveryZone>();
    public DbSet<QuoteRevision> QuoteRevisions => Set<QuoteRevision>();
    public DbSet<ApprovalDelegation> ApprovalDelegations => Set<ApprovalDelegation>();
    public DbSet<BusinessAlertRule> BusinessAlertRules => Set<BusinessAlertRule>();
    public DbSet<BusinessAlert> BusinessAlerts => Set<BusinessAlert>();
    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();
    public DbSet<AssetAttributeDefinition> AssetAttributeDefinitions => Set<AssetAttributeDefinition>();
    public DbSet<AssetAttributeValue> AssetAttributeValues => Set<AssetAttributeValue>();
    public DbSet<InspectionTemplate> InspectionTemplates => Set<InspectionTemplate>();
    public DbSet<AssetInspection> AssetInspections => Set<AssetInspection>();
    public DbSet<BranchDivisionService> BranchDivisionServices => Set<BranchDivisionService>();
    public DbSet<BranchOperatingPeriod> BranchOperatingPeriods => Set<BranchOperatingPeriod>();
    public DbSet<BranchCalendarException> BranchCalendarExceptions => Set<BranchCalendarException>();
    public DbSet<UserAccessScope> UserAccessScopes => Set<UserAccessScope>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<MfaChallenge> MfaChallenges => Set<MfaChallenge>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<InAppNotification>(e => {
            e.Property(x => x.Audience).HasMaxLength(16); e.Property(x => x.Kind).HasMaxLength(32);
            e.Property(x => x.Title).HasMaxLength(160); e.Property(x => x.Message).HasMaxLength(2000);
            e.Property(x => x.Url).HasMaxLength(600); e.Property(x => x.RequiredRole).HasMaxLength(64);
            e.Property(x => x.EventKey).HasMaxLength(450); e.HasIndex(x => x.EventKey).IsUnique();
            e.Property(x => x.Severity).HasMaxLength(16);
            e.Property(x => x.ActionType).HasMaxLength(40); e.Property(x => x.ActionLabel).HasMaxLength(80);
            e.Property(x => x.RelatedEntityType).HasMaxLength(80);
            e.HasIndex(x => new { x.RecipientUserId, x.CreatedAt });
            e.HasIndex(x => new { x.Audience, x.CustomerId, x.CreatedAt });
            e.HasIndex(x => new { x.Audience, x.BranchId, x.DivisionId, x.CreatedAt });
        });
        builder.Entity<NotificationRead>(e => { e.HasKey(x => new { x.NotificationId, x.UserId }); e.HasOne(x => x.Notification).WithMany().HasForeignKey(x => x.NotificationId); });
        builder.Entity<StaffNotificationPreference>(e => { e.HasKey(x => new { x.UserId, x.Category }); e.Property(x => x.Category).HasMaxLength(32); e.Property(x => x.EmailFrequency).HasMaxLength(16); });
        builder.Entity<NotificationEmailDelivery>(e => e.HasKey(x => new { x.NotificationId, x.UserId }));

        builder.Entity<Branch>(entity =>
        {
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(20);
            entity.Property(x => x.Name).HasMaxLength(150);
            entity.Property(x => x.Latitude).HasPrecision(10, 7); entity.Property(x => x.Longitude).HasPrecision(10, 7);
        });

        builder.Entity<Division>(entity =>
        {
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(30);
            entity.Property(x => x.Name).HasMaxLength(150);
            entity.Property(x => x.DefaultTaxRate).HasPrecision(5, 2);
            entity.Property(x => x.DefaultBondAmount).HasPrecision(18, 2);
        });
        builder.Entity<BranchDivision>(entity =>
        {
            entity.HasKey(x => new { x.BranchId, x.DivisionId });
            entity.HasOne(x => x.Branch).WithMany(x => x.Divisions).HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<BranchDivisionService>(entity => { entity.HasIndex(x => new { x.BranchId, x.DivisionId, x.ServiceOfferingId }).IsUnique(); });
        builder.Entity<BranchOperatingPeriod>(entity => { entity.HasIndex(x => new { x.BranchId, x.DayOfWeek }).IsUnique(); entity.Property(x => x.AfterHoursCharge).HasPrecision(18, 2); });
        builder.Entity<BranchCalendarException>(entity => { entity.HasIndex(x => new { x.BranchId, x.Date }).IsUnique(); entity.Property(x => x.AfterHoursCharge).HasPrecision(18, 2); });

        builder.Entity<ServiceOffering>(entity =>
        {
            entity.HasIndex(x => new { x.DivisionId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(40);
            entity.Property(x => x.Name).HasMaxLength(150);
            entity.HasOne(x => x.Division).WithMany(x => x.ServiceOfferings)
                .HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.DefaultDepositAmount).HasPrecision(18, 2);
        });
        builder.Entity<AssetCategory>(entity => { entity.HasIndex(x => new { x.DivisionId, x.Code }).IsUnique(); entity.HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(x => x.ServiceOffering).WithMany().HasForeignKey(x => x.ServiceOfferingId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<AssetAttributeDefinition>(entity => { entity.HasIndex(x => new { x.AssetCategoryId, x.Code }).IsUnique(); entity.HasOne(x => x.AssetCategory).WithMany(x => x.AttributeDefinitions).HasForeignKey(x => x.AssetCategoryId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<InspectionTemplate>(entity => { entity.HasIndex(x => new { x.AssetCategoryId, x.Name, x.Stage }).IsUnique(); entity.HasOne(x => x.AssetCategory).WithMany().HasForeignKey(x => x.AssetCategoryId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<AssetInspection>(entity => { entity.HasIndex(x => new { x.AssetId, x.CompletedAt }); entity.Property(x => x.MeterReading).HasPrecision(18,2); entity.Property(x => x.FuelPercent).HasPrecision(5,2); entity.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<ChargeDefinition>(entity =>
        {
            entity.HasIndex(x => new { x.DivisionId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(50); entity.Property(x => x.Name).HasMaxLength(150);
            entity.Property(x => x.DefaultSellingRate).HasPrecision(18, 2); entity.Property(x => x.DefaultCostRate).HasPrecision(18, 2);
            entity.HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ServiceOffering).WithMany(x => x.ChargeDefinitions).HasForeignKey(x => x.ServiceOfferingId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Asset>(entity =>
        {
            entity.HasIndex(x => x.AssetNumber).IsUnique();
            entity.Property(x => x.AssetNumber).HasMaxLength(50);
            entity.Property(x => x.Name).HasMaxLength(150);
            entity.Property(x => x.Category).HasMaxLength(100);
            entity.Property(x => x.DailyRate).HasPrecision(18, 2);
            entity.Property(x => x.DefaultBondAmount).HasPrecision(18, 2);
            entity.Property(x => x.CurrentMeterReading).HasPrecision(18, 2);
            entity.Property(x => x.AcquisitionCost).HasPrecision(18, 2);
            entity.Property(x => x.CurrentBookValue).HasPrecision(18, 2);
            entity.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ServiceOffering).WithMany().HasForeignKey(x => x.ServiceOfferingId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AssetCategory).WithMany().HasForeignKey(x => x.AssetCategoryId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<AssetAttributeValue>(entity => { entity.HasIndex(x => new { x.AssetId, x.AttributeDefinitionId }).IsUnique(); entity.HasOne(x => x.Asset).WithMany(x => x.AttributeValues).HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade); entity.HasOne(x => x.AttributeDefinition).WithMany().HasForeignKey(x => x.AttributeDefinitionId).OnDelete(DeleteBehavior.Restrict); });

        builder.Entity<AssetCostEntry>(entity =>
        {
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.AssetId, x.OccurredOn });
            entity.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Customer>(entity =>
        {
            entity.HasIndex(x => x.CustomerNumber).IsUnique();
            entity.Property(x => x.CustomerNumber).HasMaxLength(50);
            entity.Property(x => x.HirePreferences)
                .HasColumnName("HirePreference")
                .HasConversion(
                    preferences => JsonSerializer.Serialize(preferences, HirePreferenceJsonOptions),
                    stored => ParseHirePreferences(stored))
                .Metadata.SetValueComparer(new ValueComparer<List<CustomerHirePreference>>(
                    (left, right) => left != null && right != null && left.SequenceEqual(right),
                    preferences => preferences.Aggregate(0, (hash, preference) => HashCode.Combine(hash, preference)),
                    preferences => preferences.ToList()));
            entity.Property(x => x.HirePreferences).HasMaxLength(128);
            entity.Property(x => x.Email).HasMaxLength(254);
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.HasIndex(x => x.CustomerId).IsUnique().HasFilter("[CustomerId] IS NOT NULL");
            entity.HasOne(x => x.Customer).WithOne().HasForeignKey<ApplicationUser>(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<UserAccessScope>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.Type, x.DivisionId, x.BranchId, x.IsActive });
            entity.HasOne(x => x.User).WithMany(x => x.AccessScopes).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(x => x.Reason).HasMaxLength(500);
        });
        builder.Entity<RolePermission>(entity =>
        {
            entity.HasIndex(x => new { x.RoleName, x.Permission }).IsUnique();
            entity.Property(x => x.RoleName).HasMaxLength(100); entity.Property(x => x.Permission).HasMaxLength(150);
        });
        builder.Entity<UserPermissionOverride>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.Permission }).IsUnique();
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(x => x.Permission).HasMaxLength(150); entity.Property(x => x.Reason).HasMaxLength(500);
        });
        builder.Entity<SecurityEvent>(entity => { entity.HasIndex(x => new { x.UserId, x.OccurredAt }); entity.Property(x => x.Email).HasMaxLength(254); entity.Property(x => x.IpAddress).HasMaxLength(100); entity.Property(x => x.WindowId).HasMaxLength(100); });
        builder.Entity<UserSession>(entity => { entity.HasIndex(x => new { x.UserId, x.EndedAt, x.ExpiresAt }); entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); entity.Property(x => x.WindowId).HasMaxLength(100); });
        builder.Entity<MfaChallenge>(entity => { entity.HasIndex(x => new { x.UserId, x.ExpiresAt }); entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });

        builder.Entity<Booking>(entity =>
        {
            entity.HasIndex(x => x.BookingNumber).IsUnique();
            entity.Property(x => x.BookingNumber).HasMaxLength(50);
            entity.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            entity.Property(x => x.TaxRate).HasPrecision(5, 2);
            entity.Property(x => x.DepositRequired).HasPrecision(18, 2);
            entity.Property(x => x.AdditionalCharges).HasPrecision(18, 2);
            entity.Property(x => x.BondAmountHeld).HasPrecision(18, 2);
            entity.Property(x => x.BondDeductionAmount).HasPrecision(18, 2);
            entity.Property(x => x.BondRefundAmount).HasPrecision(18, 2);
        });

        builder.Entity<RentalInspection>(entity =>
        {
            entity.Property(x => x.MeterReading).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.BookingId, x.Type }).IsUnique();
        });

        builder.Entity<AuthorizedDriver>(entity =>
        {
            entity.Property(x => x.FullName).HasMaxLength(150);
            entity.Property(x => x.LicenceNumber).HasMaxLength(80);
            entity.HasIndex(x => new { x.BookingId, x.LicenceNumber }).IsUnique();
        });

        builder.Entity<RentalPayment>(entity =>
        {
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.ReceiptNumber).HasMaxLength(60);
            entity.HasIndex(x => x.ReceiptNumber).IsUnique();
        });

        builder.Entity<RentalNotification>(entity => entity.Property(x => x.Recipient).HasMaxLength(254));

        builder.Entity<RentalIncident>(entity =>
        {
            entity.Property(x => x.IncidentNumber).HasMaxLength(50);
            entity.Property(x => x.EstimatedCost).HasPrecision(18, 2);
            entity.HasIndex(x => x.IncidentNumber).IsUnique();
        });

        builder.Entity<RentalInvoice>(entity =>
        {
            entity.Property(x => x.InvoiceNumber).HasMaxLength(50);
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
            entity.Property(x => x.TaxAmount).HasPrecision(18, 2);
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.Property(x => x.AmountPaid).HasPrecision(18, 2);
            entity.Property(x => x.BalanceDue).HasPrecision(18, 2);
            entity.HasIndex(x => x.InvoiceNumber).IsUnique();
            entity.HasIndex(x => x.BookingId).IsUnique();
            entity.HasOne(x => x.Booking).WithOne(x => x.Invoice).HasForeignKey<RentalInvoice>(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<InvoiceLine>(entity=>{Money(entity,nameof(InvoiceLine.Quantity),nameof(InvoiceLine.UnitPrice),nameof(InvoiceLine.TaxRate));entity.HasOne(x=>x.Invoice).WithMany(x=>x.Lines).HasForeignKey(x=>x.InvoiceId).OnDelete(DeleteBehavior.Cascade);});
        builder.Entity<PaymentAllocation>(entity=>{entity.Property(x=>x.Amount).HasPrecision(18,2);entity.HasIndex(x=>new{x.PaymentId,x.InvoiceId});entity.HasOne(x=>x.Payment).WithMany().HasForeignKey(x=>x.PaymentId).OnDelete(DeleteBehavior.Restrict);entity.HasOne(x=>x.Invoice).WithMany().HasForeignKey(x=>x.InvoiceId).OnDelete(DeleteBehavior.Restrict);});
        builder.Entity<CreditNote>(entity=>{entity.HasIndex(x=>x.CreditNoteNumber).IsUnique();Money(entity,nameof(CreditNote.Subtotal),nameof(CreditNote.TaxAmount),nameof(CreditNote.Total));entity.HasOne(x=>x.Invoice).WithMany().HasForeignKey(x=>x.InvoiceId).OnDelete(DeleteBehavior.Restrict);});

        builder.Entity<RentalAgreementAddendum>(entity =>
        {
            entity.Property(x => x.AddendumNumber).HasMaxLength(60);
            entity.HasIndex(x => x.AddendumNumber).IsUnique();
        });

        builder.Entity<RentalAgreement>(entity =>
        {
            entity.HasIndex(x => x.AgreementNumber).IsUnique();
            entity.HasIndex(x => x.BookingId).IsUnique();
            entity.Property(x => x.AgreementNumber).HasMaxLength(50);
            entity.Property(x => x.TermsVersion).HasMaxLength(30);
            entity.HasOne(x => x.Booking).WithOne(x => x.RentalAgreement).HasForeignKey<RentalAgreement>(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MaintenanceJob>(entity =>
        {
            entity.HasIndex(x => x.JobNumber).IsUnique();
            entity.Property(x => x.JobNumber).HasMaxLength(50);
            entity.Property(x => x.EstimatedCost).HasPrecision(18, 2);
            entity.Property(x => x.ActualCost).HasPrecision(18, 2);
            entity.Property(x => x.PartsCost).HasPrecision(18, 2);
            entity.Property(x => x.LabourCost).HasPrecision(18, 2);
            entity.Property(x => x.TransportCost).HasPrecision(18, 2);
            entity.Property(x => x.ExternalServiceCost).HasPrecision(18, 2);
            entity.Property(x => x.TaxCost).HasPrecision(18, 2);
            entity.Property(x => x.OtherCost).HasPrecision(18, 2);
            entity.Property(x => x.MeterReading).HasPrecision(18, 2);
            entity.Property(x => x.NextServiceMeter).HasPrecision(18, 2);
            entity.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AuditEvent>(entity =>
        {
            entity.HasIndex(x => x.OccurredAt);
            entity.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SystemSetting>(entity =>
        {
            entity.Property(x => x.Key).HasMaxLength(100);
            entity.Property(x => x.Category).HasMaxLength(50);
            entity.HasIndex(x => x.Key).IsUnique();
        });

        builder.Entity<NotificationTemplate>(entity =>
        {
            entity.Property(x => x.Key).HasMaxLength(100);
            entity.Property(x => x.Channel).HasMaxLength(20);
            entity.HasIndex(x => x.Key).IsUnique();
        });

        builder.Entity<OutboundEmail>(entity =>
        {
            entity.Property(x => x.Recipient).HasMaxLength(254);
            entity.Property(x => x.Subject).HasMaxLength(300);
            entity.Property(x => x.ProviderMessageId).HasMaxLength(200);
            entity.HasIndex(x => new { x.Status, x.NextAttemptAt });
        });
        builder.Entity<PasswordResetOtp>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.ExpiresAt });
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<EmailVerificationOtp>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.ExpiresAt });
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<CustomerAccountActivation>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.ExpiresAt });
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        ConfigureCorporateOperations(builder);
        ConfigureBusinessOperations(builder);

        builder.Entity<BookingItem>(entity =>
        {
            entity.Property(x => x.DailyRate).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.AssetId, x.StartAt, x.EndAt });
            entity.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<BookingCharge>(entity =>
        {
            entity.Property(x => x.Quantity).HasPrecision(18, 3); entity.Property(x => x.UnitRate).HasPrecision(18, 2); entity.Property(x => x.UnitCost).HasPrecision(18, 2);
            entity.HasOne(x => x.Booking).WithMany(x => x.Charges).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ChargeDefinition).WithMany().HasForeignKey(x => x.ChargeDefinitionId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static List<CustomerHirePreference> ParseHirePreferences(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored) || stored == "NoPreference") return [];
        if (stored.StartsWith('['))
        {
            try { return JsonSerializer.Deserialize<List<CustomerHirePreference>>(stored, HirePreferenceJsonOptions) ?? []; }
            catch (JsonException) { return JsonSerializer.Deserialize<List<CustomerHirePreference>>(stored) ?? []; }
        }
        return Enum.TryParse<CustomerHirePreference>(stored, out var preference) ? [preference] : [];
    }

    private static void ConfigureCorporateOperations(ModelBuilder builder)
    {
        builder.Entity<SalesQuote>(entity => { entity.HasIndex(x => x.QuoteNumber).IsUnique(); entity.HasOne<Division>().WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict); Money(entity, nameof(SalesQuote.Subtotal), nameof(SalesQuote.Discount), nameof(SalesQuote.Tax), nameof(SalesQuote.Total)); });
        builder.Entity<CorporateAccount>(entity => { entity.HasIndex(x => x.CustomerId).IsUnique(); Money(entity, nameof(CorporateAccount.CreditLimit)); });
        builder.Entity<DispatchJob>(entity => { entity.HasIndex(x => x.DispatchNumber).IsUnique(); entity.Property(x => x.Latitude).HasPrecision(10, 7); entity.Property(x => x.Longitude).HasPrecision(10, 7); Money(entity, nameof(DispatchJob.DeliveryCharge), nameof(DispatchJob.DistanceKilometres), nameof(DispatchJob.InternalTransportCost), nameof(DispatchJob.FailedDeliveryCharge)); });
        builder.Entity<AssetTransfer>(entity => { entity.HasIndex(x => x.TransferNumber).IsUnique(); Money(entity, nameof(AssetTransfer.DepartureMeter), nameof(AssetTransfer.ArrivalMeter), nameof(AssetTransfer.TransferCost)); });
        builder.Entity<PricingRule>(entity => Money(entity, nameof(PricingRule.Rate), nameof(PricingRule.IncludedUsage), nameof(PricingRule.ExcessUsageRate), nameof(PricingRule.WeekendMultiplier), nameof(PricingRule.HolidayMultiplier), nameof(PricingRule.OvertimeMultiplier)));
        builder.Entity<ApprovalRequest>(entity => { entity.HasIndex(x => x.RequestNumber).IsUnique(); entity.Property(x => x.CurrentStage).HasDefaultValue(1); entity.Property(x => x.TotalStages).HasDefaultValue(1); Money(entity, nameof(ApprovalRequest.Amount)); entity.HasOne<ApprovalWorkflow>().WithMany().HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<ApprovalWorkflow>(entity => { entity.HasIndex(x => new { x.Type, x.BranchId, x.DivisionId, x.IsActive }); entity.Property(x => x.AppliesToBooking).HasDefaultValue(true); Money(entity, nameof(ApprovalWorkflow.MinimumAmount), nameof(ApprovalWorkflow.HireDurationDays)); });
        builder.Entity<ApprovalWorkflowStage>(entity => { entity.HasIndex(x => new { x.WorkflowId, x.Sequence }).IsUnique(); entity.HasOne(x => x.Workflow).WithMany(x => x.Stages).HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<ApprovalStageDecision>(entity => { entity.HasIndex(x => new { x.ApprovalRequestId, x.StageNumber }).IsUnique(); entity.HasOne(x => x.ApprovalRequest).WithMany(x => x.StageDecisions).HasForeignKey(x => x.ApprovalRequestId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<InventoryPart>(entity => { entity.HasIndex(x => new { x.BranchId, x.PartNumber }).IsUnique(); Money(entity, nameof(InventoryPart.UnitCost)); });
        builder.Entity<PurchaseOrder>(entity => { entity.HasIndex(x => x.PurchaseOrderNumber).IsUnique(); Money(entity, nameof(PurchaseOrder.Total)); });
        builder.Entity<TelematicsSnapshot>(entity => { entity.HasIndex(x => new { x.AssetId, x.RecordedAt }); entity.Property(x => x.Latitude).HasPrecision(10, 7); entity.Property(x => x.Longitude).HasPrecision(10, 7); Money(entity, nameof(TelematicsSnapshot.Odometer), nameof(TelematicsSnapshot.EngineHours), nameof(TelematicsSnapshot.FuelPercent)); });
        builder.Entity<CustomerCase>(entity => entity.HasIndex(x => x.CaseNumber).IsUnique());
        builder.Entity<DocumentRecord>(entity => entity.HasIndex(x => x.DocumentNumber).IsUnique());
        builder.Entity<ManagementTask>(entity => entity.HasIndex(x => new { x.IsCompleted, x.DueAt }));
    }

    private static void ConfigureBusinessOperations(ModelBuilder builder)
    {
        builder.Entity<AssetLifecycleEvent>(entity => { entity.HasIndex(x => new { x.AssetId, x.OccurredAt }); entity.Property(x => x.MeterReading).HasPrecision(18, 2); entity.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<AssetMeterReading>(entity => { entity.HasIndex(x => new { x.AssetId, x.Type, x.RecordedAt }); Money(entity, nameof(AssetMeterReading.Reading), nameof(AssetMeterReading.FuelPercent)); entity.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<Supplier>(entity => { entity.HasIndex(x => x.SupplierNumber).IsUnique(); entity.Property(x => x.SupplierNumber).HasMaxLength(50); entity.Property(x => x.Name).HasMaxLength(150); });
        builder.Entity<MaintenancePartUsage>(entity => { entity.HasIndex(x => new { x.MaintenanceJobId, x.InventoryPartId }); Money(entity, nameof(MaintenancePartUsage.Quantity), nameof(MaintenancePartUsage.UnitCost)); entity.HasOne(x => x.MaintenanceJob).WithMany().HasForeignKey(x => x.MaintenanceJobId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(x => x.InventoryPart).WithMany().HasForeignKey(x => x.InventoryPartId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<Personnel>(entity => { entity.HasIndex(x => x.EmployeeNumber).IsUnique(); Money(entity, nameof(Domain.Operations.Personnel.StandardCostRate), nameof(Domain.Operations.Personnel.OvertimeCostRate), nameof(Domain.Operations.Personnel.StandardChargeRate), nameof(Domain.Operations.Personnel.OvertimeChargeRate)); });
        builder.Entity<PersonnelQualification>(entity => { entity.HasIndex(x => new { x.PersonnelId, x.CertificateNumber }).IsUnique(); entity.HasOne(x => x.Personnel).WithMany(x => x.Qualifications).HasForeignKey(x => x.PersonnelId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<BookingPersonnelAssignment>(entity => { entity.HasIndex(x => new { x.PersonnelId, x.StartAt, x.EndAt }); Money(entity, nameof(BookingPersonnelAssignment.CustomerHourlyRate), nameof(BookingPersonnelAssignment.InternalHourlyCost)); entity.HasOne(x => x.Personnel).WithMany().HasForeignKey(x => x.PersonnelId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<PersonnelTimesheet>(entity => { entity.HasIndex(x => new { x.AssignmentId, x.WorkDate }).IsUnique(); Money(entity, nameof(PersonnelTimesheet.RegularHours), nameof(PersonnelTimesheet.OvertimeHours)); entity.HasOne(x => x.Assignment).WithMany(x => x.Timesheets).HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<DeliveryZone>(entity => Money(entity, nameof(DeliveryZone.BaseCharge), nameof(DeliveryZone.CostPerKilometre), nameof(DeliveryZone.ChargePerKilometre), nameof(DeliveryZone.FailedDeliveryCharge)));
        builder.Entity<QuoteRevision>(entity => { entity.HasIndex(x => new { x.SalesQuoteId, x.Version }).IsUnique(); entity.HasOne(x => x.SalesQuote).WithMany().HasForeignKey(x => x.SalesQuoteId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<ApprovalDelegation>(entity => entity.HasIndex(x => new { x.FromUserId, x.StartsAt, x.EndsAt }));
        builder.Entity<BusinessAlertRule>(entity => { entity.HasIndex(x => new { x.Category, x.BranchId, x.DivisionId, x.IsActive }); entity.Property(x => x.Threshold).HasPrecision(18, 2); });
        builder.Entity<BusinessAlert>(entity => { entity.HasIndex(x => new { x.AcknowledgedAt, x.RaisedAt }); entity.HasOne(x => x.Rule).WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.SetNull); });
    }

    private static void Money<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity, params string[] properties) where TEntity : class
    {
        foreach (var property in properties) entity.Property(property).HasPrecision(18, 2);
    }
}
