using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Division> Divisions => Set<Division>();
    public DbSet<ServiceOffering> ServiceOfferings => Set<ServiceOffering>();
    public DbSet<BranchDivision> BranchDivisions => Set<BranchDivision>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingItem> BookingItems => Set<BookingItem>();
    public DbSet<RentalInspection> RentalInspections => Set<RentalInspection>();
    public DbSet<RentalAgreement> RentalAgreements => Set<RentalAgreement>();
    public DbSet<AuthorizedDriver> AuthorizedDrivers => Set<AuthorizedDriver>();
    public DbSet<RentalPayment> RentalPayments => Set<RentalPayment>();
    public DbSet<RentalNotification> RentalNotifications => Set<RentalNotification>();
    public DbSet<RentalIncident> RentalIncidents => Set<RentalIncident>();
    public DbSet<RentalInvoice> RentalInvoices => Set<RentalInvoice>();
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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Branch>(entity =>
        {
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(20);
            entity.Property(x => x.Name).HasMaxLength(150);
        });

        builder.Entity<Division>(entity =>
        {
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(30);
            entity.Property(x => x.Name).HasMaxLength(150);
        });
        builder.Entity<BranchDivision>(entity =>
        {
            entity.HasKey(x => new { x.BranchId, x.DivisionId });
            entity.HasOne(x => x.Branch).WithMany(x => x.Divisions).HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ServiceOffering>(entity =>
        {
            entity.HasIndex(x => new { x.DivisionId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(40);
            entity.Property(x => x.Name).HasMaxLength(150);
            entity.HasOne(x => x.Division).WithMany(x => x.ServiceOfferings)
                .HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Asset>(entity =>
        {
            entity.HasIndex(x => x.AssetNumber).IsUnique();
            entity.Property(x => x.AssetNumber).HasMaxLength(50);
            entity.Property(x => x.Name).HasMaxLength(150);
            entity.Property(x => x.Category).HasMaxLength(100);
            entity.Property(x => x.DailyRate).HasPrecision(18, 2);
            entity.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ServiceOffering).WithMany().HasForeignKey(x => x.ServiceOfferingId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Customer>(entity =>
        {
            entity.HasIndex(x => x.CustomerNumber).IsUnique();
            entity.Property(x => x.CustomerNumber).HasMaxLength(50);
            entity.Property(x => x.Email).HasMaxLength(254);
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.HasIndex(x => x.CustomerId).IsUnique().HasFilter("[CustomerId] IS NOT NULL");
            entity.HasOne(x => x.Customer).WithOne().HasForeignKey<ApplicationUser>(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        });

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

        builder.Entity<BookingItem>(entity =>
        {
            entity.Property(x => x.DailyRate).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.AssetId, x.StartAt, x.EndAt });
            entity.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureCorporateOperations(ModelBuilder builder)
    {
        builder.Entity<SalesQuote>(entity => { entity.HasIndex(x => x.QuoteNumber).IsUnique(); Money(entity, nameof(SalesQuote.Subtotal), nameof(SalesQuote.Discount), nameof(SalesQuote.Tax), nameof(SalesQuote.Total)); });
        builder.Entity<CorporateAccount>(entity => { entity.HasIndex(x => x.CustomerId).IsUnique(); Money(entity, nameof(CorporateAccount.CreditLimit)); });
        builder.Entity<DispatchJob>(entity => { entity.HasIndex(x => x.DispatchNumber).IsUnique(); entity.Property(x => x.Latitude).HasPrecision(10, 7); entity.Property(x => x.Longitude).HasPrecision(10, 7); Money(entity, nameof(DispatchJob.DeliveryCharge)); });
        builder.Entity<AssetTransfer>(entity => { entity.HasIndex(x => x.TransferNumber).IsUnique(); Money(entity, nameof(AssetTransfer.DepartureMeter), nameof(AssetTransfer.ArrivalMeter), nameof(AssetTransfer.TransferCost)); });
        builder.Entity<PricingRule>(entity => Money(entity, nameof(PricingRule.Rate), nameof(PricingRule.IncludedUsage), nameof(PricingRule.ExcessUsageRate)));
        builder.Entity<ApprovalRequest>(entity => { entity.HasIndex(x => x.RequestNumber).IsUnique(); Money(entity, nameof(ApprovalRequest.Amount)); });
        builder.Entity<InventoryPart>(entity => { entity.HasIndex(x => new { x.BranchId, x.PartNumber }).IsUnique(); Money(entity, nameof(InventoryPart.UnitCost)); });
        builder.Entity<PurchaseOrder>(entity => { entity.HasIndex(x => x.PurchaseOrderNumber).IsUnique(); Money(entity, nameof(PurchaseOrder.Total)); });
        builder.Entity<TelematicsSnapshot>(entity => { entity.HasIndex(x => new { x.AssetId, x.RecordedAt }); entity.Property(x => x.Latitude).HasPrecision(10, 7); entity.Property(x => x.Longitude).HasPrecision(10, 7); Money(entity, nameof(TelematicsSnapshot.Odometer), nameof(TelematicsSnapshot.EngineHours), nameof(TelematicsSnapshot.FuelPercent)); });
        builder.Entity<CustomerCase>(entity => entity.HasIndex(x => x.CaseNumber).IsUnique());
        builder.Entity<DocumentRecord>(entity => entity.HasIndex(x => x.DocumentNumber).IsUnique());
        builder.Entity<ManagementTask>(entity => entity.HasIndex(x => new { x.IsCompleted, x.DueAt }));
    }

    private static void Money<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity, params string[] properties) where TEntity : class
    {
        foreach (var property in properties) entity.Property(property).HasPrecision(18, 2);
    }
}
