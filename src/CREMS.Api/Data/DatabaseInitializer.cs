using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Corporate;
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
            await SeedDevelopmentDataAsync(db, userManager, cancellationToken);
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
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        if (!await db.SystemSettings.AnyAsync(cancellationToken))
        {
            db.SystemSettings.AddRange(
                new SystemSetting { Key = "rentals.vatRate", Value = "15", Category = "Rental", Description = "Default VAT percentage applied to new rentals." },
                new SystemSetting { Key = "rentals.defaultDeposit", Value = "500", Category = "Rental", Description = "Default security deposit in FJD." },
                new SystemSetting { Key = "rentals.bookingPrefix", Value = "BK", Category = "Numbering", Description = "Booking reference prefix." },
                new SystemSetting { Key = "rentals.agreementPrefix", Value = "RA", Category = "Numbering", Description = "Rental agreement reference prefix." },
                new SystemSetting { Key = "security.lockoutMinutes", Value = "30", Category = "Security", Description = "Administrative display value for account lockout duration." },
                new SystemSetting { Key = "security.maxFailedAttempts", Value = "5", Category = "Security", Description = "Administrative display value for failed sign-in threshold." },
                new SystemSetting { Key = "data.retentionYears", Value = "7", Category = "Compliance", Description = "Target retention period for rental and audit records." });
        }
        if (!await db.NotificationTemplates.AnyAsync(cancellationToken))
        {
            db.NotificationTemplates.AddRange(
                new NotificationTemplate { Key = "booking.confirmed", Name = "Booking confirmation", Channel = "Email", Subject = "Your Carpenters Rentals booking {{bookingNumber}}", Body = "Your booking is confirmed. Collection: {{startAt}} at {{branchName}}." },
                new NotificationTemplate { Key = "rental.pickup", Name = "Pickup and agreement", Channel = "Email", Subject = "Signed agreement {{agreementNumber}}", Body = "Your rental has been collected. Your signed agreement is attached." },
                new NotificationTemplate { Key = "rental.returnReminder", Name = "Return reminder", Channel = "Sms", Subject = "Rental return reminder", Body = "Reminder: {{bookingNumber}} is due for return at {{endAt}}. Contact {{branchPhone}} if you need assistance." },
                new NotificationTemplate { Key = "invoice.final", Name = "Final invoice", Channel = "Email", Subject = "Final invoice {{invoiceNumber}}", Body = "Your rental is complete. Total {{total}}; balance due {{balanceDue}}." });
        }
        await db.SaveChangesAsync(cancellationToken);

        var divisionSeeds = new[]
        {
            new DivisionSeed("MOTORS", "Carpenters Motors & Rentals", "Vehicle rental, fleet operations, servicing and parts.", DivisionCapabilities.Rental | DivisionCapabilities.Maintenance,
                new[] { new ServiceSeed("VEHICLE_RENTAL", "Vehicle rental", ServiceOfferingType.VehicleRental, PersonnelRequirement.None, true, false) }),
            new DivisionSeed("CARPTRAC", "Carptrac", "Heavy equipment, generators, parts and technical service. Hire availability remains configurable pending confirmation.", DivisionCapabilities.Maintenance | DivisionCapabilities.PersonnelSupportedHire,
                new[] { new ServiceSeed("EQUIPMENT_HIRE", "Equipment hire", ServiceOfferingType.EquipmentHire, PersonnelRequirement.Optional, false, true) }),
            new DivisionSeed("HARDWARE", "Carpenters Hardware", "Hardware, tools, builders equipment and materials. Tool hire remains configurable pending confirmation.", DivisionCapabilities.Retail,
                Array.Empty<ServiceSeed>()),
            new DivisionSeed("SHIPPING", "Carpenters Shipping & Logistics", "Shipping and logistics services for individual and corporate customers.", DivisionCapabilities.Logistics,
                new[] { new ServiceSeed("LOGISTICS", "Shipping and logistics enquiry", ServiceOfferingType.LogisticsService, PersonnelRequirement.None, false, true) }),
            new DivisionSeed("PROPERTY", "Carpenters Properties", "Residential and commercial property leasing.", DivisionCapabilities.PropertyLeasing | DivisionCapabilities.Maintenance,
                new[] { new ServiceSeed("PROPERTY_LEASE", "Property leasing", ServiceOfferingType.PropertyLease, PersonnelRequirement.None, false, true) }),
            new DivisionSeed("MH", "MH", "Retail division represented in the group structure; rental capabilities are not enabled.", DivisionCapabilities.Retail,
                Array.Empty<ServiceSeed>()),
        };
        foreach (var seed in divisionSeeds)
        {
            var division = await db.Divisions.Include(x => x.ServiceOfferings)
                .FirstOrDefaultAsync(x => x.Code == seed.Code, cancellationToken);
            if (division is null)
            {
                division = new Division { Code = seed.Code, Name = seed.Name, Description = seed.Description,
                    Capabilities = seed.Capabilities, IsPublic = true, IsActive = true };
                db.Divisions.Add(division);
            }
            foreach (var service in seed.Services.Where(service => division.ServiceOfferings.All(x => x.Code != service.Code)))
                division.ServiceOfferings.Add(new ServiceOffering { Code = service.Code, Name = service.Name, Type = service.Type,
                    PersonnelRequirement = service.PersonnelRequirement, IsBookableOnline = service.IsBookableOnline,
                    RequiresQuote = service.RequiresQuote, IsActive = true });
        }
        await db.SaveChangesAsync(cancellationToken);

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
        var divisions = await db.Divisions.ToDictionaryAsync(x => x.Code, cancellationToken);
        var services = await db.ServiceOfferings.ToDictionaryAsync(x => x.Code, cancellationToken);
        foreach (var branch in branches.Values)
        {
            foreach (var divisionCode in new[] { "MOTORS", "CARPTRAC", "HARDWARE", "SHIPPING", "PROPERTY", "MH" })
            {
                var division = divisions[divisionCode];
                if (!await db.BranchDivisions.AnyAsync(x => x.BranchId == branch.Id && x.DivisionId == division.Id, cancellationToken))
                    db.BranchDivisions.Add(new BranchDivision { BranchId = branch.Id, DivisionId = division.Id, IsActive = true });
            }
        }
        await db.SaveChangesAsync(cancellationToken);

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
            new AssetSeed("EQP-SUV-2003", "25T Mobile Crane", AssetType.Equipment, "SUV", null, "CRN-25014", 1450m, 1),
            new AssetSeed("EQP-NAD-2003", "5T Rough Terrain Forklift", AssetType.Equipment, "NAD", null, "RTF-24062", 620m, 2),
            new AssetSeed("EQP-LAU-2003", "12m Telehandler", AssetType.Equipment, "LAU", null, "TEL-25008", 780m, 2),
            new AssetSeed("EQP-LAB-2003", "3T Diesel Forklift", AssetType.Equipment, "LAB", null, "FLT-24173", 410m, 2),
            new AssetSeed("EQP-SUV-2004", "20T Hydraulic Excavator", AssetType.Equipment, "SUV", null, "EXC-25021", 1180m, 1),
            new AssetSeed("EQP-NAD-2004", "100 kVA Silent Diesel Generator", AssetType.Equipment, "NAD", null, "GEN-25036", 470m, 3),
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
            asset.DivisionId = seed.Type == AssetType.Vehicle ? divisions["MOTORS"].Id : divisions["CARPTRAC"].Id;
            asset.ServiceOfferingId = seed.Type == AssetType.Vehicle ? services["VEHICLE_RENTAL"].Id : services["EQUIPMENT_HIRE"].Id;
            asset.Category = seed.Type == AssetType.Vehicle ? "Vehicle" : "Heavy equipment";
            asset.PersonnelRequirement = seed.Type == AssetType.Equipment &&
                (seed.Name.Contains("Excavator") || seed.Name.Contains("Backhoe") || seed.Name.Contains("Crane") || seed.Name.Contains("Telehandler"))
                ? PersonnelRequirement.Required : PersonnelRequirement.None;
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

        const string developmentPassword = "CremsTest!2026";
        var developmentUsers = new[]
        {
            new DevelopmentUserSeed("rentals.manager.suva@crems.local", "Laisenia Vakalalabure", SystemRoles.BranchManager, "MOTORS", "SUV", null),
            new DevelopmentUserSeed("rentals.officer.suva@crems.local", "Kelera Waqa", SystemRoles.RentalOfficer, "MOTORS", "SUV", null),
            new DevelopmentUserSeed("rentals.manager.nadi@crems.local", "Rohit Prasad", SystemRoles.BranchManager, "MOTORS", "NAD", null),
            new DevelopmentUserSeed("rentals.officer.nadi@crems.local", "Alipate Tuisese", SystemRoles.RentalOfficer, "MOTORS", "NAD", null),
            new DevelopmentUserSeed("carptrac.manager.suva@crems.local", "Mereoni Bale", SystemRoles.BranchManager, "CARPTRAC", "SUV", null),
            new DevelopmentUserSeed("carptrac.officer.suva@crems.local", "Savenaca Driu", SystemRoles.RentalOfficer, "CARPTRAC", "SUV", null),
            new DevelopmentUserSeed("carptrac.manager.nadi@crems.local", "Anish Chand", SystemRoles.BranchManager, "CARPTRAC", "NAD", null),
            new DevelopmentUserSeed("carptrac.officer.nadi@crems.local", "Ilisapeci Raloga", SystemRoles.RentalOfficer, "CARPTRAC", "NAD", null),
            new DevelopmentUserSeed("arieta.vula@customer.example", "Arieta Vula", SystemRoles.Customer, null, null, "CUS-000001"),
            new DevelopmentUserSeed("rakesh.kumar@customer.example", "Rakesh Kumar", SystemRoles.Customer, null, null, "CUS-000004"),
            new DevelopmentUserSeed("hire@pacificcivil.example", "Pacific Civil Works", SystemRoles.Customer, null, null, "BUS-000001"),
            new DevelopmentUserSeed("fleet@coralcoasttours.example", "Coral Coast Tours", SystemRoles.Customer, null, null, "BUS-000004"),
        };
        foreach (var seed in developmentUsers)
        {
            Guid? divisionId = seed.DivisionCode is null ? null : divisions[seed.DivisionCode].Id;
            Guid? branchId = seed.BranchCode is null ? null : branches[seed.BranchCode].Id;
            Guid? customerId = seed.CustomerNumber is null ? null : seededCustomers[seed.CustomerNumber].Id;
            await SeedDevelopmentUserAsync(userManager, seed, developmentPassword, divisionId, branchId, customerId);
        }

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

        if (!await db.PricingRules.AnyAsync(cancellationToken))
        {
            db.PricingRules.AddRange(
                new PricingRule { Name = "Standard vehicle daily", AssetType = "Vehicle", Period = RatePeriod.Daily, Rate = 185m, IncludedUsage = 200m, ExcessUsageRate = 0.85m, MinimumDuration = 1 },
                new PricingRule { Name = "Standard equipment weekly", AssetType = "Equipment", Period = RatePeriod.Weekly, Rate = 2100m, IncludedUsage = 45m, ExcessUsageRate = 35m, MinimumDuration = 1 },
                new PricingRule { Name = "Pacific Civil contracted equipment", CustomerId = seededCustomers["BUS-000001"].Id, AssetType = "Equipment", Period = RatePeriod.Weekly, Rate = 1950m, IncludedUsage = 50m, ExcessUsageRate = 32m, MinimumDuration = 2 });
            db.CorporateAccounts.AddRange(
                new CorporateAccount { CustomerId = seededCustomers["BUS-000001"].Id, LegalName = "Pacific Civil Works Ltd", TaxIdentificationNumber = "TIN-71-45821", CreditLimit = 50000m, PaymentTermsDays = 30, PurchaseOrderRequired = true, BillingContactJson = "{\"name\":\"Accounts Payable\",\"email\":\"accounts@pacificcivil.example\"}", AuthorizedContactsJson = "[]", JobSitesJson = "[\"Vuda Project Yard\",\"Suva Civil Depot\"]", ContractPricingJson = "{}" },
                new CorporateAccount { CustomerId = seededCustomers["BUS-000005"].Id, LegalName = "Viti Freight Services Ltd", TaxIdentificationNumber = "TIN-71-52816", CreditLimit = 35000m, PaymentTermsDays = 14, PurchaseOrderRequired = true, BillingContactJson = "{\"name\":\"Finance Team\",\"email\":\"finance@vitifreight.example\"}", AuthorizedContactsJson = "[]", JobSitesJson = "[\"Walu Bay Depot\"]", ContractPricingJson = "{}" });
            db.InventoryParts.AddRange(
                new InventoryPart { PartNumber = "FLT-OIL-15W40", Name = "Heavy-duty engine oil 15W-40 (20L)", BranchId = branches["SUV"].Id, Supplier = "Carpenters Parts", UnitCost = 186m, QuantityOnHand = 8, QuantityAllocated = 2, ReorderLevel = 4 },
                new InventoryPart { PartNumber = "FLT-FILTER-OIL", Name = "Fleet oil filter", BranchId = branches["SUV"].Id, Supplier = "Carpenters Parts", UnitCost = 38m, QuantityOnHand = 5, QuantityAllocated = 2, ReorderLevel = 5 },
                new InventoryPart { PartNumber = "EQP-BAT-12V", Name = "Heavy equipment battery 12V", BranchId = branches["NAD"].Id, Supplier = "Industrial Battery Fiji", UnitCost = 465m, QuantityOnHand = 1, ReorderLevel = 2 });
            db.CustomerCases.Add(new CustomerCase { CaseNumber = "CASE-2026-0001", CustomerId = seededCustomers["BUS-000003"].Id, BranchId = branches["LAB"].Id, Type = CaseType.Breakdown, Status = CaseStatus.InProgress, Priority = CasePriority.High, Subject = "Compactor pull-start failure at job site", Description = "Customer reported that the unit will not start after normal pre-start checks.", DueAt = DateTimeOffset.UtcNow.AddHours(4) });
            db.ManagementTasks.AddRange(
                new ManagementTask { BranchId = branches["LAB"].Id, Category = TaskCategory.OverdueRental, Priority = TaskPriority.Critical, Title = "Contact customer about overdue rental BK-2026-0005", DueAt = DateTimeOffset.UtcNow.AddHours(1) },
                new ManagementTask { BranchId = branches["NAD"].Id, Category = TaskCategory.LowStock, Priority = TaskPriority.High, Title = "Reorder heavy equipment batteries", DueAt = DateTimeOffset.UtcNow.AddDays(1) },
                new ManagementTask { BranchId = branches["SUV"].Id, Category = TaskCategory.QuoteFollowUp, Priority = TaskPriority.Normal, Title = "Follow up corporate equipment enquiry", DueAt = DateTimeOffset.UtcNow.AddDays(2) });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded) return;

        var errors = string.Join("; ", result.Errors.Select(error => error.Description));
        throw new InvalidOperationException($"Unable to {operation}: {errors}");
    }

    private static async Task SeedDevelopmentUserAsync(UserManager<ApplicationUser> userManager,
        DevelopmentUserSeed seed, string password, Guid? divisionId, Guid? branchId, Guid? customerId)
    {
        var user = await userManager.FindByEmailAsync(seed.Email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = seed.Email, Email = seed.Email, EmailConfirmed = true, FullName = seed.FullName,
                DivisionId = divisionId, BranchId = branchId, CustomerId = customerId, IsActive = true,
                MustChangePassword = false,
            };
            EnsureSucceeded(await userManager.CreateAsync(user, password), $"create development user {seed.Email}");
        }
        else
        {
            user.FullName = seed.FullName; user.DivisionId = divisionId; user.BranchId = branchId;
            user.CustomerId = customerId; user.IsActive = true; user.EmailConfirmed = true;
            EnsureSucceeded(await userManager.UpdateAsync(user), $"update development user {seed.Email}");
        }
        var currentRoles = await userManager.GetRolesAsync(user);
        if (currentRoles.Count > 0 && !currentRoles.Contains(seed.Role))
            EnsureSucceeded(await userManager.RemoveFromRolesAsync(user, currentRoles), $"correct roles for {seed.Email}");
        if (!await userManager.IsInRoleAsync(user, seed.Role))
            EnsureSucceeded(await userManager.AddToRoleAsync(user, seed.Role), $"assign {seed.Role} to {seed.Email}");
    }

    private sealed record BranchSeed(string Code, string Name, string Address, string Phone);
    private sealed record DivisionSeed(string Code, string Name, string Description, DivisionCapabilities Capabilities, IReadOnlyList<ServiceSeed> Services);
    private sealed record ServiceSeed(string Code, string Name, ServiceOfferingType Type, PersonnelRequirement PersonnelRequirement, bool IsBookableOnline, bool RequiresQuote);
    private sealed record AssetSeed(
        string AssetNumber, string Name, AssetType Type, string BranchCode,
        string? RegistrationNumber, string? SerialNumber, decimal DailyRate, int ServiceMonths);
    private sealed record CustomerSeed(
        string CustomerNumber, CustomerType Type, string Name, string Email,
        string Phone, string Address, string IdentificationNumber);
    private sealed record BookingSeed(
        string BookingNumber, string CustomerNumber, string AssetNumber,
        BookingStatus Status, int StartOffsetDays, int EndOffsetDays);
    private sealed record DevelopmentUserSeed(string Email, string FullName, string Role,
        string? DivisionCode, string? BranchCode, string? CustomerNumber);
}
