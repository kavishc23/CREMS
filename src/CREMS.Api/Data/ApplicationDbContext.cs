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

