using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        await db.Database.MigrateAsync(cancellationToken);

        foreach (var roleName in SystemRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
                EnsureSucceeded(roleResult, $"create the {roleName} role");
            }
        }

        if (app.Environment.IsDevelopment())
        {
            await SeedDevelopmentDataAsync(db, cancellationToken);
        }

        var email = app.Configuration["BootstrapAdmin:Email"];
        var password = app.Configuration["BootstrapAdmin:Password"];
        var fullName = app.Configuration["BootstrapAdmin:FullName"] ?? "System Administrator";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            app.Logger.LogWarning(
                "No bootstrap administrator was configured. Set BootstrapAdmin credentials using user secrets before first use.");
            return;
        }

        var administrator = await userManager.FindByEmailAsync(email);
        if (administrator is null)
        {
            administrator = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                IsActive = true,
            };

            var createResult = await userManager.CreateAsync(administrator, password);
            EnsureSucceeded(createResult, "create the bootstrap administrator");
        }

        if (!await userManager.IsInRoleAsync(administrator, SystemRoles.Administrator))
        {
            var roleResult = await userManager.AddToRoleAsync(administrator, SystemRoles.Administrator);
            EnsureSucceeded(roleResult, "assign the Administrator role");
        }
    }

    private static async Task SeedDevelopmentDataAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var branchSeeds = new[]
        {
            new BranchSeed("SUV", "Suva", "61–63 Foster Road, Walu Bay, Suva", "+679 229 5055"),
            new BranchSeed("NAD", "Nadi", "WHL Business Warehouse, Nadi Back Road, Nadi", "+679 229 6128"),
            new BranchSeed("LAU", "Lautoka", "Velovelo, Queens Road, Lautoka", "+679 229 5830"),
            new BranchSeed("LAB", "Labasa", "Rosawa Street, Labasa", "+679 229 6079"),
        };

        var existingBranchCodes = await db.Branches
            .Select(branch => branch.Code)
            .ToListAsync(cancellationToken);
        foreach (var seed in branchSeeds.Where(seed => !existingBranchCodes.Contains(seed.Code)))
        {
            db.Branches.Add(new Branch
            {
                Code = seed.Code,
                Name = seed.Name,
                Address = seed.Address,
                Phone = seed.Phone,
                IsActive = true,
            });
        }
        await db.SaveChangesAsync(cancellationToken);

        var branches = await db.Branches
            .Where(branch => branchSeeds.Select(seed => seed.Code).Contains(branch.Code))
            .ToDictionaryAsync(branch => branch.Code, cancellationToken);

        var legacyAssetNumbers = new Dictionary<string, string>
        {
            ["DEMO-VEH-001"] = "VEH-SUV-1001", ["DEMO-VEH-002"] = "VEH-NAD-1001",
            ["DEMO-VEH-003"] = "VEH-LAU-1001", ["DEMO-VEH-004"] = "VEH-SUV-1002",
            ["DEMO-VEH-005"] = "VEH-LAB-1001", ["DEMO-EQP-001"] = "EQP-SUV-2001",
            ["DEMO-EQP-002"] = "EQP-LAU-2001", ["DEMO-EQP-003"] = "EQP-NAD-2001",
            ["DEMO-EQP-004"] = "EQP-LAB-2001", ["DEMO-EQP-005"] = "EQP-SUV-2002",
        };
        var legacyAssets = await db.Assets
            .Where(asset => legacyAssetNumbers.Keys.Contains(asset.AssetNumber))
            .ToListAsync(cancellationToken);
        foreach (var asset in legacyAssets) asset.AssetNumber = legacyAssetNumbers[asset.AssetNumber];
        await db.SaveChangesAsync(cancellationToken);

        var assetSeeds = new[]
        {
            new AssetSeed("VEH-SUV-1001", "Nissan Navara VL 4x4 Manual", AssetType.Vehicle, "SUV", "LR 421", null, 185m, 2),
            new AssetSeed("VEH-NAD-1001", "Hyundai Tucson GLS 2WD", AssetType.Vehicle, "NAD", "LT 782", null, 165m, 3),
            new AssetSeed("VEH-LAU-1001", "Isuzu D-Max 3.0L Automatic", AssetType.Vehicle, "LAU", "LT 615", null, 180m, 1),
            new AssetSeed("VEH-SUV-1002", "Nissan NV350 Urvan 16 Seater", AssetType.Vehicle, "SUV", "LR 936", null, 240m, 2),
            new AssetSeed("VEH-LAB-1001", "Isuzu NPR Crew Cab Cargo Tray", AssetType.Vehicle, "LAB", "LT 354", null, 320m, 1),
            new AssetSeed("VEH-SUV-1003", "Nissan X-Trail e-Power 7 Seater", AssetType.Vehicle, "SUV", "LR 528", null, 215m, 4),
            new AssetSeed("VEH-NAD-1002", "Hyundai Grand i10 Sedan", AssetType.Vehicle, "NAD", "LT 847", null, 105m, 5),
            new AssetSeed("VEH-LAU-1002", "Honda CR-V Sport AWD", AssetType.Vehicle, "LAU", "LT 691", null, 225m, 2),
            new AssetSeed("VEH-LAB-1002", "Hyundai Staria 11 Seater Automatic", AssetType.Vehicle, "LAB", "LT 438", null, 255m, 3),
            new AssetSeed("VEH-SUV-1004", "Isuzu NQR75L 37 Seater Coach", AssetType.Vehicle, "SUV", "LR 773", null, 550m, 1),
            new AssetSeed("VEH-NAD-1003", "Honda ZR-V LX", AssetType.Vehicle, "NAD", "LT 905", null, 195m, 4),
            new AssetSeed("VEH-LAU-1003", "BAIC X55 Elite", AssetType.Vehicle, "LAU", "LT 729", null, 175m, 3),
            new AssetSeed("EQP-SUV-2001", "3.5T Mini Excavator", AssetType.Equipment, "SUV", null, "EXC-24017", 475m, 1),
            new AssetSeed("EQP-LAU-2001", "2.5T Diesel Forklift", AssetType.Equipment, "LAU", null, "FLT-23108", 350m, 2),
            new AssetSeed("EQP-NAD-2001", "60 kVA Diesel Generator", AssetType.Equipment, "NAD", null, "GEN-24126", 285m, 4),
            new AssetSeed("EQP-LAB-2001", "Reversible Plate Compactor", AssetType.Equipment, "LAB", null, "COM-22049", 95m, 3),
            new AssetSeed("EQP-SUV-2002", "Electric Scissor Lift 10m", AssetType.Equipment, "SUV", null, "LFT-23087", 390m, 2),
            new AssetSeed("EQP-NAD-2002", "Towable Air Compressor 185 CFM", AssetType.Equipment, "NAD", null, "AIR-24112", 210m, 3),
            new AssetSeed("EQP-LAU-2002", "Backhoe Loader", AssetType.Equipment, "LAU", null, "BHL-22031", 650m, 1),
            new AssetSeed("EQP-LAB-2002", "Concrete Mixer 350L", AssetType.Equipment, "LAB", null, "MIX-24044", 120m, 5),
        };

        var existingAssets = await db.Assets
            .Where(asset => assetSeeds.Select(seed => seed.AssetNumber).Contains(asset.AssetNumber))
            .ToDictionaryAsync(asset => asset.AssetNumber, cancellationToken);
        foreach (var seed in assetSeeds)
        {
            if (!branches.TryGetValue(seed.BranchCode, out var branch)) continue;
            if (!existingAssets.TryGetValue(seed.AssetNumber, out var asset))
            {
                asset = new Asset { AssetNumber = seed.AssetNumber, Name = seed.Name };
                db.Assets.Add(asset);
            }
            asset.Name = seed.Name;
            asset.Type = seed.Type;
            asset.Status = asset.Status == AssetStatus.Rented ? AssetStatus.Rented : AssetStatus.Available;
            asset.BranchId = branch.Id;
            asset.RegistrationNumber = seed.RegistrationNumber;
            asset.SerialNumber = seed.SerialNumber;
            asset.DailyRate = seed.DailyRate;
            asset.NextServiceDate ??= DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(seed.ServiceMonths));
            asset.IsActive = true;
        }

        var legacyCustomerNumbers = new Dictionary<string, string>
        {
            ["DEMO-CUS-001"] = "CUS-000001", ["DEMO-CUS-002"] = "CUS-000002",
            ["DEMO-CUS-003"] = "CUS-000003", ["DEMO-BUS-001"] = "BUS-000001",
            ["DEMO-BUS-002"] = "BUS-000002",
        };
        var legacyCustomers = await db.Customers
            .Where(customer => legacyCustomerNumbers.Keys.Contains(customer.CustomerNumber))
            .ToListAsync(cancellationToken);
        foreach (var customer in legacyCustomers) customer.CustomerNumber = legacyCustomerNumbers[customer.CustomerNumber];
        await db.SaveChangesAsync(cancellationToken);

        var customerSeeds = new[]
        {
            new CustomerSeed("CUS-000001", CustomerType.Individual, "Arieta Vula", "arieta.vula@customer.example", "+679 992 4101", "Laucala Bay, Suva", "DL-458210"),
            new CustomerSeed("CUS-000002", CustomerType.Individual, "Jone Ratu", "jone.ratu@customer.example", "+679 991 2740", "Namaka, Nadi", "DL-472905"),
            new CustomerSeed("CUS-000003", CustomerType.Individual, "Mere Tawake", "mere.tawake@customer.example", "+679 990 6382", "Naseakula, Labasa", "DL-491726"),
            new CustomerSeed("BUS-000001", CustomerType.Business, "Pacific Civil Works Ltd", "hire@pacificcivil.example", "+679 995 3021", "Vuda, Lautoka", "TIN-71-45821"),
            new CustomerSeed("BUS-000002", CustomerType.Business, "Island Events & Logistics Ltd", "operations@islandevents.example", "+679 998 1446", "Walu Bay, Suva", "TIN-71-49206"),
            new CustomerSeed("CUS-000004", CustomerType.Individual, "Rakesh Kumar", "rakesh.kumar@customer.example", "+679 934 8261", "Martintar, Nadi", "DL-463188"),
            new CustomerSeed("CUS-000005", CustomerType.Individual, "Ana Marama", "ana.marama@customer.example", "+679 977 0534", "Samabula, Suva", "DL-480357"),
            new CustomerSeed("CUS-000006", CustomerType.Individual, "Samuela Nacewa", "samuela.nacewa@customer.example", "+679 936 7158", "Waiyavi, Lautoka", "DL-475602"),
            new CustomerSeed("BUS-000003", CustomerType.Business, "Northern Builders Ltd", "plant@northernbuilders.example", "+679 988 6512", "Nasekula Road, Labasa", "TIN-71-50684"),
            new CustomerSeed("BUS-000004", CustomerType.Business, "Coral Coast Tours Ltd", "fleet@coralcoasttours.example", "+679 972 4480", "Queens Road, Nadi", "TIN-71-51739"),
            new CustomerSeed("BUS-000005", CustomerType.Business, "Viti Freight Services Ltd", "dispatch@vitifreight.example", "+679 933 2917", "Walu Bay, Suva", "TIN-71-52816"),
            new CustomerSeed("CUS-000007", CustomerType.Individual, "Litia Rokotui", "litia.rokotui@customer.example", "+679 940 1836", "Field 40, Lautoka", "DL-487233"),
        };

        var existingCustomers = await db.Customers
            .Where(customer => customerSeeds.Select(seed => seed.CustomerNumber).Contains(customer.CustomerNumber))
            .ToDictionaryAsync(customer => customer.CustomerNumber, cancellationToken);
        foreach (var seed in customerSeeds)
        {
            if (!existingCustomers.TryGetValue(seed.CustomerNumber, out var customer))
            {
                customer = new Customer { CustomerNumber = seed.CustomerNumber, Name = seed.Name };
                db.Customers.Add(customer);
            }
            customer.Type = seed.Type;
            customer.Name = seed.Name;
            customer.Email = seed.Email;
            customer.Phone = seed.Phone;
            customer.Address = seed.Address;
            customer.IdentificationNumber = seed.IdentificationNumber;
            customer.IsActive = true;
            customer.IsBlocked = false;
        }

        await db.SaveChangesAsync(cancellationToken);

        var seededCustomers = await db.Customers
            .Where(customer => customerSeeds.Select(seed => seed.CustomerNumber).Contains(customer.CustomerNumber))
            .ToDictionaryAsync(customer => customer.CustomerNumber, cancellationToken);
        var seededAssets = await db.Assets
            .Where(asset => assetSeeds.Select(seed => seed.AssetNumber).Contains(asset.AssetNumber))
            .ToDictionaryAsync(asset => asset.AssetNumber, cancellationToken);
        var bookingSeeds = new[]
        {
            new BookingSeed("BK-2026-0001", "CUS-000001", "VEH-SUV-1003", BookingStatus.Completed, -45, -40),
            new BookingSeed("BK-2026-0002", "BUS-000001", "EQP-LAU-2002", BookingStatus.Completed, -32, -25),
            new BookingSeed("BK-2026-0003", "BUS-000004", "VEH-NAD-1002", BookingStatus.Completed, -18, -14),
            new BookingSeed("BK-2026-0004", "CUS-000004", "VEH-NAD-1001", BookingStatus.ConvertedToRental, -2, 3),
            new BookingSeed("BK-2026-0005", "BUS-000003", "EQP-LAB-2001", BookingStatus.ConvertedToRental, -5, -1),
            new BookingSeed("BK-2026-0006", "CUS-000005", "VEH-SUV-1001", BookingStatus.Confirmed, 2, 6),
            new BookingSeed("BK-2026-0007", "BUS-000005", "VEH-SUV-1002", BookingStatus.Confirmed, 5, 9),
            new BookingSeed("REQ-2026-0008", "CUS-000007", "VEH-LAU-1002", BookingStatus.Draft, 8, 11),
            new BookingSeed("REQ-2026-0009", "BUS-000002", "EQP-SUV-2002", BookingStatus.Draft, 12, 16),
        };
        var existingBookingNumbers = await db.Bookings.Select(booking => booking.BookingNumber)
            .ToListAsync(cancellationToken);
        foreach (var seed in bookingSeeds.Where(seed => !existingBookingNumbers.Contains(seed.BookingNumber)))
        {
            if (!seededCustomers.TryGetValue(seed.CustomerNumber, out var customer) ||
                !seededAssets.TryGetValue(seed.AssetNumber, out var asset)) continue;
            var start = DateTimeOffset.UtcNow.Date.AddDays(seed.StartOffsetDays);
            var end = DateTimeOffset.UtcNow.Date.AddDays(seed.EndOffsetDays);
            db.Bookings.Add(new Booking
            {
                BookingNumber = seed.BookingNumber,
                CustomerId = customer.Id,
                BranchId = asset.BranchId,
                Status = seed.Status,
                Notes = "Created as part of the development operating dataset.",
                TaxRate = 15m,
                DepositRequired = asset.Type == AssetType.Vehicle ? 500m : 1000m,
                CreatedAt = start.AddDays(-3),
                Items = [new BookingItem { AssetId = asset.Id, StartAt = start, EndAt = end, DailyRate = asset.DailyRate }],
            });
            if (seed.Status == BookingStatus.ConvertedToRental) asset.Status = AssetStatus.Rented;
        }
        await db.SaveChangesAsync(cancellationToken);

        if (!await db.MaintenanceJobs.AnyAsync(cancellationToken))
        {
            var maintenanceSeeds = new[]
            {
                new { Asset = "VEH-SUV-1001", Type = "Scheduled service", Fault = "10,000 km preventive service and safety inspection", Assigned = "Fleet Workshop — Suva", Estimate = 420m, Status = MaintenanceStatus.Completed },
                new { Asset = "EQP-NAD-2001", Type = "Electrical diagnosis", Fault = "Intermittent low-voltage warning under load", Assigned = "Nadi Equipment Workshop", Estimate = 680m, Status = MaintenanceStatus.InProgress },
                new { Asset = "VEH-LAB-1001", Type = "Corrective repair", Fault = "Rear tray latch requires replacement and alignment", Assigned = "Northern Fleet Services", Estimate = 310m, Status = MaintenanceStatus.WaitingForParts },
            };
            foreach (var seed in maintenanceSeeds)
            {
                if (!seededAssets.TryGetValue(seed.Asset, out var asset)) continue;
                var completed = seed.Status == MaintenanceStatus.Completed;
                db.MaintenanceJobs.Add(new MaintenanceJob
                {
                    JobNumber = $"MNT-2026-{db.MaintenanceJobs.Local.Count + 1:0000}", AssetId = asset.Id,
                    BranchId = asset.BranchId, Status = seed.Status, ServiceType = seed.Type,
                    FaultDescription = seed.Fault, AssignedTo = seed.Assigned, EstimatedCost = seed.Estimate,
                    ActualCost = completed ? seed.Estimate - 35m : null, PartsUsed = completed ? "Engine oil, oil filter and inspection consumables" : null,
                    ReportedAt = DateTimeOffset.UtcNow.AddDays(completed ? -30 : -4),
                    CompletedAt = completed ? DateTimeOffset.UtcNow.AddDays(-29) : null,
                    NextServiceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
                });
                if (!completed) asset.Status = AssetStatus.Maintenance;
            }
            db.AuditEvents.Add(new AuditEvent { UserId = Guid.Empty, UserName = "CREMS System",
                Action = "Operational data initialized", EntityType = "System", EntityId = Guid.Empty,
                Summary = "Initial maintenance and rental workflow records were created." });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded) return;

        var errors = string.Join("; ", result.Errors.Select(error => error.Description));
        throw new InvalidOperationException($"Unable to {operation}: {errors}");
    }

    private sealed record BranchSeed(string Code, string Name, string Address, string Phone);
    private sealed record AssetSeed(
        string AssetNumber, string Name, AssetType Type, string BranchCode,
        string? RegistrationNumber, string? SerialNumber, decimal DailyRate, int ServiceMonths);
    private sealed record CustomerSeed(
        string CustomerNumber, CustomerType Type, string Name, string Email,
        string Phone, string Address, string IdentificationNumber);
    private sealed record BookingSeed(
        string BookingNumber, string CustomerNumber, string AssetNumber,
        BookingStatus Status, int StartOffsetDays, int EndOffsetDays);
}
