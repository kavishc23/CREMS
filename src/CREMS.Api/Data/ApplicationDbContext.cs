using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
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
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingItem> BookingItems => Set<BookingItem>();
    public DbSet<RentalInspection> RentalInspections => Set<RentalInspection>();
    public DbSet<RentalAgreement> RentalAgreements => Set<RentalAgreement>();
    public DbSet<MaintenanceJob> MaintenanceJobs => Set<MaintenanceJob>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Branch>(entity =>
        {
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(20);
            entity.Property(x => x.Name).HasMaxLength(150);
        });

        builder.Entity<Asset>(entity =>
        {
            entity.HasIndex(x => x.AssetNumber).IsUnique();
            entity.Property(x => x.AssetNumber).HasMaxLength(50);
            entity.Property(x => x.Name).HasMaxLength(150);
            entity.Property(x => x.DailyRate).HasPrecision(18, 2);
            entity.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Customer>(entity =>
        {
            entity.HasIndex(x => x.CustomerNumber).IsUnique();
            entity.Property(x => x.CustomerNumber).HasMaxLength(50);
            entity.Property(x => x.Email).HasMaxLength(254);
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

        builder.Entity<RentalAgreement>(entity =>
        {
            entity.HasIndex(x => x.AgreementNumber).IsUnique();
            entity.HasIndex(x => x.BookingId).IsUnique();
            entity.Property(x => x.AgreementNumber).HasMaxLength(50);
            entity.Property(x => x.TermsVersion).HasMaxLength(30);
            entity.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
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

        builder.Entity<BookingItem>(entity =>
        {
            entity.Property(x => x.DailyRate).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.AssetId, x.StartAt, x.EndAt });
            entity.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
