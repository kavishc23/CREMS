using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Domain.Operations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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
        await SeedRolePermissionsAsync(db, cancellationToken);
        await DisableLegacyDriverAccessAsync(db, roleManager, userManager, cancellationToken);
        await RemoveLegacyFinanceAccessAsync(db, roleManager, userManager, cancellationToken);

        // Development seed data is substantial. Populate a new database once instead
        // of re-reading and updating the complete demo dataset on every API restart.
        // Set DevelopmentData:RefreshOnStartup=true only when seed definitions need
        // to be deliberately reapplied to an existing development database.
        var refreshDevelopmentData = app.Configuration.GetValue<bool>("DevelopmentData:RefreshOnStartup");
        var developmentDataMissing = app.Environment.IsDevelopment() &&
            (!await db.Divisions.AsNoTracking().AnyAsync(cancellationToken) ||
             !await db.Assets.AsNoTracking().AnyAsync(cancellationToken));
        if (app.Environment.IsDevelopment() && (refreshDevelopmentData || developmentDataMissing))
        {
            await SeedDevelopmentDataAsync(db, userManager, app.Environment.ContentRootPath, cancellationToken);
        }
        await NormalizeCustomersAsync(db, cancellationToken);
        await EnsureDefaultBookingApprovalRuleAsync(db, cancellationToken);

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

        if (!await userManager.IsInRoleAsync(administrator, SystemRoles.SuperAdministrator))
        {
            var roleResult = await userManager.AddToRoleAsync(administrator, SystemRoles.SuperAdministrator);
            EnsureSucceeded(roleResult, "assign the Super Administrator role");
        }
    }

    private static async Task EnsureDefaultBookingApprovalRuleAsync(ApplicationDbContext db, CancellationToken token)
    {
        if (await db.ApprovalWorkflows.AnyAsync(x => x.Type == ApprovalType.Booking, token)) return;
        db.ApprovalWorkflows.Add(new ApprovalWorkflow
        {
            Name = "Equipment, personnel and overtime approval",
            Type = ApprovalType.Booking,
            EntityType = nameof(Booking),
            TriggerForEquipment = true,
            TriggerForPersonnel = true,
            TriggerForOvertime = true,
            Priority = 100,
            IsActive = true,
            Stages =
            [
                new ApprovalWorkflowStage
                {
                    Sequence = 1,
                    Name = "Branch manager approval",
                    AssignedRole = SystemRoles.BranchManager,
                    EscalateAfterHours = 24,
                    EscalationRole = SystemRoles.Administrator,
                },
            ],
        });
        await db.SaveChangesAsync(token);
    }

    private static async Task SeedDevelopmentDataAsync(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        string contentRootPath,
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
        await PurgeRemovedDevelopmentDataAsync(db, cancellationToken);

        var divisionSeeds = new[]
        {
            new DivisionSeed("MOTORS", "Carpenters Motors & Rentals", "Vehicle rental, fleet operations, servicing and parts.", DivisionCapabilities.Rental | DivisionCapabilities.Maintenance,
                new[] { new ServiceSeed("VEHICLE_RENTAL", "Vehicle rental", ServiceOfferingType.VehicleRental, PersonnelRequirement.None, true, false) }),
            new DivisionSeed("CARPTRAC", "Carptrac", "Caterpillar construction equipment, forklifts, power generation and technical support.", DivisionCapabilities.Maintenance | DivisionCapabilities.PersonnelSupportedHire,
                new[] { new ServiceSeed("EQUIPMENT_HIRE", "Equipment hire", ServiceOfferingType.EquipmentHire, PersonnelRequirement.Optional, false, true) }),
            new DivisionSeed("SHIPPING", "Carpenters Shipping", "Portable toilets, scaffolding and big-bin hire with delivery and collection support.", DivisionCapabilities.Rental | DivisionCapabilities.Logistics | DivisionCapabilities.Maintenance,
                new[]
                {
                    new ServiceSeed("PORTABLE_TOILET_HIRE", "Portable toilet hire", ServiceOfferingType.EquipmentHire, PersonnelRequirement.None, false, true, ChargeUnit.Unit, true),
                    new ServiceSeed("SCAFFOLDING_HIRE", "Scaffolding hire", ServiceOfferingType.EquipmentHire, PersonnelRequirement.Optional, false, true, ChargeUnit.SquareMetre, true),
                    new ServiceSeed("BIG_BIN_HIRE", "Big-bin hire", ServiceOfferingType.EquipmentHire, PersonnelRequirement.None, false, true, ChargeUnit.Unit, true),
                }),
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
            else
            {
                division.Name = seed.Name; division.Description = seed.Description;
                division.Capabilities = seed.Capabilities; division.IsActive = true; division.IsPublic = true;
            }
            foreach (var service in seed.Services.Where(service => division.ServiceOfferings.All(x => x.Code != service.Code)))
            {
                var offering = new ServiceOffering { Division = division, DivisionId = division.Id, Code = service.Code, Name = service.Name, Type = service.Type,
                    PersonnelRequirement = service.PersonnelRequirement, IsBookableOnline = service.IsBookableOnline,
                    RequiresQuote = service.RequiresQuote, DefaultHireUnit = service.DefaultHireUnit,
                    RequiresDelivery = service.RequiresDelivery, IsActive = true };
                db.ServiceOfferings.Add(offering);
                division.ServiceOfferings.Add(offering);
            }
            foreach (var service in division.ServiceOfferings.Where(x => seed.Services.Any(s => s.Code == x.Code)))
            {
                var serviceSeed = seed.Services.Single(x => x.Code == service.Code);
                service.Name = serviceSeed.Name; service.Type = serviceSeed.Type;
                service.PersonnelRequirement = serviceSeed.PersonnelRequirement;
                service.IsBookableOnline = serviceSeed.IsBookableOnline; service.RequiresQuote = serviceSeed.RequiresQuote;
                service.DefaultHireUnit = serviceSeed.DefaultHireUnit; service.RequiresDelivery = serviceSeed.RequiresDelivery;
                service.IsActive = true;
            }
        }
        var shippingDivision = await db.Divisions.Include(x => x.ServiceOfferings)
            .SingleAsync(x => x.Code == "SHIPPING", cancellationToken);
        var shippingServiceCodes = divisionSeeds.Single(x => x.Code == "SHIPPING").Services.Select(x => x.Code).ToHashSet();
        foreach (var obsoleteService in shippingDivision.ServiceOfferings.Where(x => !shippingServiceCodes.Contains(x.Code)))
        {
            obsoleteService.IsActive = false;
            obsoleteService.IsBookableOnline = false;
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
        var divisions = await db.Divisions.Where(x => x.IsActive).ToDictionaryAsync(x => x.Code, cancellationToken);
        var services = await db.ServiceOfferings.Where(x => x.IsActive).ToDictionaryAsync(x => x.Code, cancellationToken);
        foreach (var branch in branches.Values)
        {
            var operatingDivisionCodes = branch.Code is "SUV" or "LAU"
                ? new[] { "MOTORS", "CARPTRAC", "SHIPPING" }
                : new[] { "MOTORS", "CARPTRAC" };
            foreach (var divisionCode in operatingDivisionCodes)
            {
                var division = divisions[divisionCode];
                if (!await db.BranchDivisions.AnyAsync(x => x.BranchId == branch.Id && x.DivisionId == division.Id, cancellationToken))
                    db.BranchDivisions.Add(new BranchDivision { BranchId = branch.Id, DivisionId = division.Id, IsActive = true });
            }
        }
        var shippingBranchIds = new[] { branches["SUV"].Id, branches["LAU"].Id };
        var staleShippingBranches = await db.BranchDivisions
            .Where(x => x.DivisionId == shippingDivision.Id && !shippingBranchIds.Contains(x.BranchId))
            .ToListAsync(cancellationToken);
        foreach (var assignment in staleShippingBranches) assignment.IsActive = false;
        var obsoleteShippingServiceIds = shippingDivision.ServiceOfferings.Where(x => !x.IsActive).Select(x => x.Id).ToArray();
        var obsoleteShippingBranchServices = await db.BranchDivisionServices
            .Where(x => obsoleteShippingServiceIds.Contains(x.ServiceOfferingId))
            .ToListAsync(cancellationToken);
        foreach (var assignment in obsoleteShippingBranchServices) { assignment.IsActive = false; assignment.IsBookable = false; }
        foreach (var branchId in shippingBranchIds)
        {
            foreach (var service in shippingDivision.ServiceOfferings.Where(x => x.IsActive))
            {
                var assignment = await db.BranchDivisionServices.FirstOrDefaultAsync(
                    x => x.BranchId == branchId && x.DivisionId == shippingDivision.Id && x.ServiceOfferingId == service.Id,
                    cancellationToken);
                if (assignment is null)
                    db.BranchDivisionServices.Add(new BranchDivisionService { BranchId = branchId,
                        DivisionId = shippingDivision.Id, ServiceOfferingId = service.Id, IsActive = true, IsBookable = true });
                else { assignment.IsActive = true; assignment.IsBookable = true; }
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        var chargeSeeds = new[]
        {
            new { Division = "MOTORS", Service = "VEHICLE_RENTAL", Code = "BASE_DAILY", Name = "Vehicle hire", Category = ChargeCategory.BaseHire, Unit = ChargeUnit.Day, Sell = 0m, Cost = 0m, Required = true },
            new { Division = "MOTORS", Service = "VEHICLE_RENTAL", Code = "DRIVER_HOUR", Name = "Professional driver", Category = ChargeCategory.Driver, Unit = ChargeUnit.Hour, Sell = 28m, Cost = 18m, Required = false },
            new { Division = "CARPTRAC", Service = "EQUIPMENT_HIRE", Code = "BASE_EQUIPMENT_DAY", Name = "Equipment hire", Category = ChargeCategory.BaseHire, Unit = ChargeUnit.Day, Sell = 0m, Cost = 0m, Required = true },
            new { Division = "CARPTRAC", Service = "EQUIPMENT_HIRE", Code = "OPERATOR_HOUR", Name = "Certified equipment operator", Category = ChargeCategory.Operator, Unit = ChargeUnit.Hour, Sell = 42m, Cost = 27m, Required = false },
            new { Division = "CARPTRAC", Service = "EQUIPMENT_HIRE", Code = "EQUIPMENT_DELIVERY", Name = "Equipment delivery / collection", Category = ChargeCategory.Transport, Unit = ChargeUnit.Trip, Sell = 250m, Cost = 165m, Required = false },
            new { Division = "SHIPPING", Service = "PORTABLE_TOILET_HIRE", Code = "PORTABLE_TOILET_UNIT", Name = "Portable toilet hire", Category = ChargeCategory.BaseHire, Unit = ChargeUnit.Unit, Sell = 0m, Cost = 0m, Required = true },
            new { Division = "SHIPPING", Service = "SCAFFOLDING_HIRE", Code = "SCAFFOLD_SQM", Name = "Scaffolding hire", Category = ChargeCategory.BaseHire, Unit = ChargeUnit.SquareMetre, Sell = 0m, Cost = 0m, Required = true },
            new { Division = "SHIPPING", Service = "BIG_BIN_HIRE", Code = "BIG_BIN_UNIT", Name = "Big-bin hire", Category = ChargeCategory.BaseHire, Unit = ChargeUnit.Unit, Sell = 0m, Cost = 0m, Required = true },
            new { Division = "SHIPPING", Service = "BIG_BIN_HIRE", Code = "SHIPPING_DELIVERY", Name = "Delivery and collection", Category = ChargeCategory.Transport, Unit = ChargeUnit.Trip, Sell = 0m, Cost = 0m, Required = true },
        };
        foreach (var seed in chargeSeeds)
        {
            var division = divisions[seed.Division]; var service = services[seed.Service];
            if (await db.ChargeDefinitions.AnyAsync(x => x.DivisionId == division.Id && x.Code == seed.Code, cancellationToken)) continue;
            db.ChargeDefinitions.Add(new ChargeDefinition { DivisionId = division.Id, ServiceOfferingId = service.Id, Code = seed.Code, Name = seed.Name, Category = seed.Category, Unit = seed.Unit, DefaultSellingRate = seed.Sell, DefaultCostRate = seed.Cost, IsRequired = seed.Required, IsTaxable = true, IsCustomerVisible = true, IsActive = true });
        }
        await db.SaveChangesAsync(cancellationToken);

        // These categories and checklists mirror the operational documents supplied by
        // Carpenters. They remain data-driven so the business can revise a form without
        // changing the rental workflow or deploying new application code.
        var categorySeeds = new[]
        {
            new AssetCategorySeed("RENTAL_VEHICLE", "Rental vehicle", "MOTORS", "VEHICLE_RENTAL", "Odometer",
                PersonnelRequirement.None,
                [
                    new("SEATS", "Seats", AttributeDataType.Integer, null, true, true),
                    new("TRANSMISSION", "Transmission", AttributeDataType.Choice, null, true, true, OptionsJson: "[\"Automatic\",\"Manual\"]"),
                    new("FUEL_TYPE", "Fuel type", AttributeDataType.Choice, null, true, true, OptionsJson: "[\"Petrol\",\"Diesel\",\"Hybrid\",\"Electric\"]"),
                    new("COLOUR", "Colour", AttributeDataType.Text, null, false, true),
                    new("FIRST_REGISTERED", "Date first registered", AttributeDataType.Date, null, false, false),
                ]),
            new AssetCategorySeed("FORKLIFT", "Forklift", "CARPTRAC", "EQUIPMENT_HIRE", "EngineHours",
                PersonnelRequirement.Optional,
                [
                    new("LIFT_CAPACITY", "Lift capacity", AttributeDataType.Number, "tonne", true, true),
                    new("MAX_LIFT_HEIGHT", "Maximum lift height", AttributeDataType.Number, "m", false, true),
                    new("POWER_TYPE", "Power type", AttributeDataType.Choice, null, true, true, OptionsJson: "[\"Diesel\",\"LPG\",\"Electric\"]"),
                ]),
            new AssetCategorySeed("GENSET", "Generator set", "CARPTRAC", "EQUIPMENT_HIRE", "EngineHours",
                PersonnelRequirement.Optional,
                [
                    new("OUTPUT_KVA", "Rated output", AttributeDataType.Number, "kVA", true, true),
                    new("VOLTAGE", "Voltage", AttributeDataType.Number, "V", false, true),
                    new("PHASE", "Phase", AttributeDataType.Choice, null, false, true, OptionsJson: "[\"Single phase\",\"Three phase\"]"),
                    new("FUEL_CAPACITY", "Fuel capacity", AttributeDataType.Number, "L", false, false),
                ]),
            new AssetCategorySeed("HEAVY_MACHINE", "Heavy machine", "CARPTRAC", "EQUIPMENT_HIRE", "EngineHours",
                PersonnelRequirement.Required,
                [
                    new("OPERATING_WEIGHT", "Operating weight", AttributeDataType.Number, "tonne", false, true),
                    new("ATTACHMENTS", "Attachments", AttributeDataType.Text, null, false, true),
                    new("TRACKED", "Tracked machine", AttributeDataType.Boolean, null, false, true),
                ]),
            new AssetCategorySeed("GENERAL_EQUIPMENT", "General hire equipment", "CARPTRAC", "EQUIPMENT_HIRE", "OperatingHours",
                PersonnelRequirement.Optional,
                [
                    new("POWER_SUPPLY", "Power supply", AttributeDataType.Text, null, false, true),
                    new("CAPACITY", "Capacity", AttributeDataType.Text, null, false, true),
                ]),
            new AssetCategorySeed("PORTABLE_TOILET", "Portable toilet", "SHIPPING", "PORTABLE_TOILET_HIRE", "RentalDays",
                PersonnelRequirement.None,
                [
                    new("UNIT_TYPE", "Unit type", AttributeDataType.Choice, null, true, true, OptionsJson: "[\"Standard\",\"Accessible\",\"Handwash station\"]"),
                    new("SERVICE_FREQUENCY", "Service frequency", AttributeDataType.Choice, null, true, false, OptionsJson: "[\"On request\",\"Weekly\",\"Twice weekly\"]"),
                    new("WASTE_CAPACITY", "Waste tank capacity", AttributeDataType.Number, "L", false, false),
                ]),
            new AssetCategorySeed("SCAFFOLD", "Scaffolding", "SHIPPING", "SCAFFOLDING_HIRE", "Units",
                PersonnelRequirement.Optional,
                [
                    new("SYSTEM_TYPE", "Scaffold system", AttributeDataType.Choice, null, true, true, OptionsJson: "[\"Frame\",\"Ringlock\",\"Mobile tower\"]"),
                    new("COVERAGE", "Coverage", AttributeDataType.Number, "m²", true, true),
                    new("MAX_HEIGHT", "Maximum configured height", AttributeDataType.Number, "m", false, true),
                    new("INSTALLATION_REQUIRED", "Installation required", AttributeDataType.Boolean, null, false, false),
                ]),
            new AssetCategorySeed("BIG_BIN", "Big bin", "SHIPPING", "BIG_BIN_HIRE", "RentalDays",
                PersonnelRequirement.None,
                [
                    new("CAPACITY", "Bin capacity", AttributeDataType.Number, "m³", true, true),
                    new("WASTE_TYPE", "Permitted waste", AttributeDataType.Text, null, true, true),
                    new("MAX_LOAD", "Maximum load", AttributeDataType.Number, "tonne", false, false),
                ]),
        };

        foreach (var seed in categorySeeds)
        {
            var division = divisions[seed.DivisionCode];
            var service = services[seed.ServiceCode];
            var category = await db.AssetCategories.Include(x => x.AttributeDefinitions)
                .FirstOrDefaultAsync(x => x.DivisionId == division.Id && x.Code == seed.Code, cancellationToken);
            if (category is null)
            {
                category = new AssetCategory { DivisionId = division.Id, ServiceOfferingId = service.Id,
                    Code = seed.Code, Name = seed.Name, Description = $"Configured from the Carpenters operational stock lists and inspection forms.",
                    DefaultMeterType = seed.MeterType, PersonnelRequirement = seed.PersonnelRequirement, IsActive = true };
                db.AssetCategories.Add(category);
            }
            else
            {
                category.Name = seed.Name; category.ServiceOfferingId = service.Id;
                category.DefaultMeterType = seed.MeterType; category.PersonnelRequirement = seed.PersonnelRequirement;
                category.IsActive = true;
            }
            foreach (var attribute in seed.Attributes.Where(attribute => category.AttributeDefinitions.All(x => x.Code != attribute.Code)))
                category.AttributeDefinitions.Add(new AssetAttributeDefinition { Code = attribute.Code, Name = attribute.Name,
                    DataType = attribute.DataType, Unit = attribute.Unit, IsRequired = attribute.IsRequired,
                    IsSearchable = attribute.IsSearchable, IsCustomerVisible = attribute.IsCustomerVisible,
                    DisplayOrder = category.AttributeDefinitions.Count + 1, OptionsJson = attribute.OptionsJson });
        }
        await db.SaveChangesAsync(cancellationToken);

        var categories = await db.AssetCategories
            .Where(x => categorySeeds.Select(seed => seed.Code).Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, cancellationToken);
        var allowedShippingCategoryCodes = new[] { "PORTABLE_TOILET", "SCAFFOLD", "BIG_BIN" };
        var obsoleteShippingCategories = await db.AssetCategories
            .Where(x => x.DivisionId == shippingDivision.Id && !allowedShippingCategoryCodes.Contains(x.Code))
            .ToListAsync(cancellationToken);
        foreach (var category in obsoleteShippingCategories) category.IsActive = false;
        var templateSeeds = CreateCarpentersInspectionTemplates();
        foreach (var seed in templateSeeds)
        {
            var category = categories[seed.CategoryCode];
            var template = await db.InspectionTemplates.FirstOrDefaultAsync(
                x => x.AssetCategoryId == category.Id && x.Name == seed.Name && x.Stage == seed.Stage,
                cancellationToken);
            if (template is null)
            {
                template = new InspectionTemplate { AssetCategoryId = category.Id, Name = seed.Name, Stage = seed.Stage };
                db.InspectionTemplates.Add(template);
            }
            template.ChecklistJson = BuildChecklistJson(seed.Sections);
            template.RequiresCustomerSignature = seed.RequiresCustomerSignature;
            template.RequiresStaffSignature = true;
            template.IsActive = true;
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
            new AssetSeed("EQP-SUV-2001", "CAT 303.5E CR Mini Excavator", AssetType.Equipment, "SUV", null, "CAT3035-24017", 475m, 1),
            new AssetSeed("EQP-LAU-2001", "CAT DP25N 2.5T Diesel Forklift", AssetType.Equipment, "LAU", null, "CATDP25-23108", 350m, 2),
            new AssetSeed("EQP-NAD-2001", "CAT DE65E0 60 kVA Generator", AssetType.Equipment, "NAD", null, "CATDE65-24126", 285m, 4),
            new AssetSeed("EQP-LAB-2001", "CAT CB2.5 Utility Compactor", AssetType.Equipment, "LAB", null, "CATCB25-22049", 295m, 3),
            new AssetSeed("EQP-SUV-2002", "CAT DP30N 3T Diesel Forklift", AssetType.Equipment, "SUV", null, "CATDP30-23087", 390m, 2),
            new AssetSeed("EQP-NAD-2002", "CAT DE110E2 100 kVA Generator", AssetType.Equipment, "NAD", null, "CATDE110-24112", 470m, 3),
            new AssetSeed("EQP-LAU-2002", "CAT 428 Backhoe Loader", AssetType.Equipment, "LAU", null, "CAT428-22031", 650m, 1),
            new AssetSeed("EQP-LAB-2002", "Concrete Mixer 350L", AssetType.Equipment, "LAB", null, "MIX-24044", 120m, 5),
            new AssetSeed("EQP-SUV-2003", "CAT TH408D 4T Telehandler", AssetType.Equipment, "SUV", null, "CATTH408-25014", 780m, 1),
            new AssetSeed("EQP-NAD-2003", "CAT DP50CN 5T Diesel Forklift", AssetType.Equipment, "NAD", null, "CATDP50-24062", 620m, 2),
            new AssetSeed("EQP-LAU-2003", "CAT 320 Hydraulic Excavator", AssetType.Equipment, "LAU", null, "CAT320-25008", 1180m, 2),
            new AssetSeed("EQP-LAB-2003", "CAT DP30N 3T Diesel Forklift", AssetType.Equipment, "LAB", null, "CATDP30-24173", 410m, 2),
            new AssetSeed("EQP-SUV-2004", "CAT 330 Hydraulic Excavator", AssetType.Equipment, "SUV", null, "CAT330-25021", 1450m, 1),
            new AssetSeed("EQP-NAD-2004", "CAT DE150E0 135 kVA Generator", AssetType.Equipment, "NAD", null, "CATDE150-25036", 595m, 3),
            new AssetSeed("SHP-SUV-3001", "Standard Portable Toilet PT-101", AssetType.Equipment, "SUV", null, "PT-101", 35m, 1),
            new AssetSeed("SHP-SUV-3002", "Accessible Portable Toilet PT-102", AssetType.Equipment, "SUV", null, "PT-102", 48m, 1),
            new AssetSeed("SHP-SUV-3101", "Frame Scaffolding Set SF-201", AssetType.Equipment, "SUV", null, "SF-201", 160m, 2),
            new AssetSeed("SHP-LAU-3101", "Mobile Scaffold Tower SF-202", AssetType.Equipment, "LAU", null, "SF-202", 120m, 2),
            new AssetSeed("SHP-SUV-3201", "10 m³ General-Waste Big Bin BB-301", AssetType.Equipment, "SUV", null, "BB-301", 95m, 2),
            new AssetSeed("SHP-LAU-3201", "15 m³ General-Waste Big Bin BB-302", AssetType.Equipment, "LAU", null, "BB-302", 125m, 2),
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
            var isShippingAsset = seed.AssetNumber.StartsWith("SHP-", StringComparison.Ordinal);
            asset.DivisionId = seed.Type == AssetType.Vehicle ? divisions["MOTORS"].Id
                : isShippingAsset ? divisions["SHIPPING"].Id : divisions["CARPTRAC"].Id;
            var categoryCode = seed.Type == AssetType.Vehicle ? "RENTAL_VEHICLE"
                : seed.Name.Contains("Portable Toilet", StringComparison.OrdinalIgnoreCase) ? "PORTABLE_TOILET"
                : seed.Name.Contains("Scaffold", StringComparison.OrdinalIgnoreCase) ? "SCAFFOLD"
                : seed.Name.Contains("Big Bin", StringComparison.OrdinalIgnoreCase) ? "BIG_BIN"
                : seed.Name.Contains("Forklift", StringComparison.OrdinalIgnoreCase) ? "FORKLIFT"
                : seed.Name.Contains("Generator", StringComparison.OrdinalIgnoreCase) ? "GENSET"
                : seed.Name.Contains("Excavator", StringComparison.OrdinalIgnoreCase) ||
                  seed.Name.Contains("Backhoe", StringComparison.OrdinalIgnoreCase) ||
                  seed.Name.Contains("Compactor", StringComparison.OrdinalIgnoreCase) ||
                  seed.Name.Contains("Telehandler", StringComparison.OrdinalIgnoreCase) ? "HEAVY_MACHINE"
                : "GENERAL_EQUIPMENT";
            asset.AssetCategoryId = categories[categoryCode].Id;
            asset.ServiceOfferingId = categoryCode switch
            {
                "RENTAL_VEHICLE" => services["VEHICLE_RENTAL"].Id,
                "PORTABLE_TOILET" => services["PORTABLE_TOILET_HIRE"].Id,
                "SCAFFOLD" => services["SCAFFOLDING_HIRE"].Id,
                "BIG_BIN" => services["BIG_BIN_HIRE"].Id,
                _ => services["EQUIPMENT_HIRE"].Id,
            };
            asset.Category = categories[categoryCode].Name;
            asset.PersonnelRequirement = seed.Type == AssetType.Equipment &&
                (seed.Name.Contains("Excavator") || seed.Name.Contains("Backhoe") || seed.Name.Contains("Crane") || seed.Name.Contains("Telehandler"))
                ? PersonnelRequirement.Required : PersonnelRequirement.None;
            if (categoryCode == "SCAFFOLD") asset.PersonnelRequirement = PersonnelRequirement.Optional;
            asset.Status = asset.Status == AssetStatus.Rented ? AssetStatus.Rented : AssetStatus.Available;
            asset.BranchId = branch.Id;
            asset.RegistrationNumber = seed.RegistrationNumber;
            asset.SerialNumber = seed.SerialNumber;
            var nameParts = seed.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            asset.Manufacturer = seed.Type == AssetType.Vehicle ? nameParts[0] : seed.Name.StartsWith("CAT ") ? "Caterpillar" : nameParts[0];
            asset.Model = seed.Type == AssetType.Vehicle
                ? string.Join(' ', nameParts.Skip(1).Take(Math.Min(3, nameParts.Length - 1)))
                : string.Join(' ', nameParts.Skip(1).Take(Math.Min(2, nameParts.Length - 1)));
            var stableSeed = seed.AssetNumber.Aggregate(17, (value, character) => value * 31 + character);
            stableSeed = Math.Abs(stableSeed == int.MinValue ? 0 : stableSeed);
            asset.ModelYear ??= 2021 + stableSeed % 5;
            asset.MeterUnit = seed.Type == AssetType.Vehicle ? "km"
                : categoryCode is "PORTABLE_TOILET" or "BIG_BIN" ? "days"
                : categoryCode == "SCAFFOLD" ? "units" : "hours";
            asset.CurrentMeterReading ??= seed.Type == AssetType.Vehicle
                ? 18_000 + stableSeed % 72_000
                : 450 + stableSeed % 4_800;
            asset.AcquisitionDate ??= new DateOnly(asset.ModelYear.Value, 2 + stableSeed % 9, 15);
            asset.AcquisitionCost = asset.AcquisitionCost > 0 ? asset.AcquisitionCost : seed.Type == AssetType.Vehicle ? seed.DailyRate * 310m : seed.DailyRate * 520m;
            asset.CurrentBookValue ??= Math.Round(asset.AcquisitionCost * 0.72m, 2);
            asset.OwnershipType ??= "Owned";
            asset.InsurancePolicyNumber ??= $"FLEET-{seed.AssetNumber}";
            asset.InsuranceExpiry ??= DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(8));
            asset.CurrentLocation = branch.Name;
            asset.DailyRate = seed.DailyRate;
            asset.NextServiceDate ??= DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(seed.ServiceMonths));
            asset.IsActive = true;
        }

        var allowedShippingCategoryIds = new[] { categories["PORTABLE_TOILET"].Id, categories["SCAFFOLD"].Id, categories["BIG_BIN"].Id };
        var obsoleteShippingAssets = await db.Assets
            .Where(x => x.DivisionId == shippingDivision.Id &&
                (!x.AssetCategoryId.HasValue || !allowedShippingCategoryIds.Contains(x.AssetCategoryId.Value)))
            .ToListAsync(cancellationToken);
        foreach (var asset in obsoleteShippingAssets) { asset.IsActive = false; asset.Status = AssetStatus.Retired; }
        await db.SaveChangesAsync(cancellationToken);

        var vehicleAttributeSeeds = new Dictionary<string, (string Seats, string Transmission, string FuelType)>
        {
            ["VEH-SUV-1001"] = ("5", "Manual", "Diesel"),
            ["VEH-NAD-1001"] = ("5", "Automatic", "Petrol"),
            ["VEH-LAU-1001"] = ("5", "Automatic", "Diesel"),
            ["VEH-SUV-1002"] = ("16", "Manual", "Diesel"),
            ["VEH-LAB-1001"] = ("6", "Manual", "Diesel"),
            ["VEH-SUV-1003"] = ("7", "Automatic", "Hybrid"),
            ["VEH-NAD-1002"] = ("5", "Automatic", "Petrol"),
            ["VEH-LAU-1002"] = ("5", "Automatic", "Petrol"),
            ["VEH-LAB-1002"] = ("11", "Automatic", "Diesel"),
            ["VEH-SUV-1004"] = ("37", "Manual", "Diesel"),
            ["VEH-NAD-1003"] = ("5", "Automatic", "Hybrid"),
            ["VEH-LAU-1003"] = ("5", "Automatic", "Petrol"),
        };
        var vehicleCategoryId = categories["RENTAL_VEHICLE"].Id;
        var vehicleDefinitions = await db.AssetAttributeDefinitions
            .Where(definition => definition.AssetCategoryId == vehicleCategoryId &&
                new[] { "SEATS", "TRANSMISSION", "FUEL_TYPE" }.Contains(definition.Code))
            .ToDictionaryAsync(definition => definition.Code, cancellationToken);
        var seededVehicleAssets = await db.Assets
            .Where(asset => vehicleAttributeSeeds.Keys.Contains(asset.AssetNumber))
            .ToDictionaryAsync(asset => asset.AssetNumber, cancellationToken);
        var seededVehicleIds = seededVehicleAssets.Values.Select(asset => asset.Id).ToArray();
        var existingVehicleAttributes = await db.AssetAttributeValues
            .Where(value => seededVehicleIds.Contains(value.AssetId))
            .ToListAsync(cancellationToken);
        foreach (var (assetNumber, specification) in vehicleAttributeSeeds)
        {
            if (!seededVehicleAssets.TryGetValue(assetNumber, out var asset)) continue;
            foreach (var (code, value) in new[]
            {
                ("SEATS", specification.Seats),
                ("TRANSMISSION", specification.Transmission),
                ("FUEL_TYPE", specification.FuelType),
            })
            {
                if (!vehicleDefinitions.TryGetValue(code, out var definition)) continue;
                var attribute = existingVehicleAttributes.FirstOrDefault(item =>
                    item.AssetId == asset.Id && item.AttributeDefinitionId == definition.Id);
                if (attribute is null)
                {
                    attribute = new AssetAttributeValue { AssetId = asset.Id, AttributeDefinitionId = definition.Id };
                    db.AssetAttributeValues.Add(attribute);
                    existingVehicleAttributes.Add(attribute);
                }
                attribute.Value = value;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await PurgeRetiredAssetsAsync(db, cancellationToken);

        var legacyCustomerNumbers = new Dictionary<string, string>
        {
            ["DEMO-CUS-001"] = "CUS-000001", ["DEMO-CUS-002"] = "CUS-000002",
            ["DEMO-CUS-003"] = "CUS-000003", ["DEMO-BUS-001"] = "LEGACY-CUS-000001",
            ["DEMO-BUS-002"] = "LEGACY-CUS-000002",
        };
        var legacyCustomers = await db.Customers
            .Where(customer => legacyCustomerNumbers.Keys.Contains(customer.CustomerNumber))
            .ToListAsync(cancellationToken);
        foreach (var customer in legacyCustomers) customer.CustomerNumber = legacyCustomerNumbers[customer.CustomerNumber];
        await db.SaveChangesAsync(cancellationToken);

        var customerSeeds = new[]
        {
            new CustomerSeed("CUS-000001", CustomerType.Individual, "Arieta Vula", "customer.arieta@crems.local", "+679 992 4101", "Laucala Bay, Suva", "TEST-DL-458210"),
            new CustomerSeed("CUS-000002", CustomerType.Individual, "Pacific Civil Works Ltd", "customer.pacificcivil@crems.local", "+679 995 3021", "Vuda, Lautoka", "TEST-TIN-71-45821"),
            new CustomerSeed("CUS-000003", CustomerType.Individual, "Island Events & Logistics Ltd", "customer.islandevents@crems.local", "+679 998 1446", "Walu Bay, Suva", "TEST-TIN-71-49206"),
            new CustomerSeed("CUS-000004", CustomerType.Individual, "Rakesh Kumar", "customer.rakesh@crems.local", "+679 934 8261", "Martintar, Nadi", "TEST-DL-463188"),
            new CustomerSeed("CUS-000005", CustomerType.Individual, "Ana Marama", "customer.ana@crems.local", "+679 977 0534", "Samabula, Suva", "TEST-DL-480357"),
            new CustomerSeed("CUS-000006", CustomerType.Individual, "Northern Builders Ltd", "customer.northernbuilders@crems.local", "+679 988 6512", "Nasekula Road, Labasa", "TEST-TIN-71-50684"),
            new CustomerSeed("CUS-000007", CustomerType.Individual, "Coral Coast Tours Ltd", "customer.coralcoast@crems.local", "+679 972 4480", "Queens Road, Nadi", "TEST-TIN-71-51739"),
            new CustomerSeed("CUS-000008", CustomerType.Individual, "Viti Freight Services Ltd", "customer.vitifreight@crems.local", "+679 933 2917", "Walu Bay, Suva", "TEST-TIN-71-52816"),
            new CustomerSeed("CUS-000009", CustomerType.Individual, "Litia Rokotui", "customer.litia@crems.local", "+679 940 1836", "Field 40, Lautoka", "TEST-DL-487233"),
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

        // Remove superseded development-only customer rows when they have never been used.
        // Referenced records are retained but disabled so rental history is never orphaned.
        var supersededCustomerNumbers = new[] { "CUS-000002", "CUS-000003", "CUS-000006" };
        var supersededCustomers = await db.Customers
            .Where(x => supersededCustomerNumbers.Contains(x.CustomerNumber))
            .ToListAsync(cancellationToken);
        foreach (var customer in supersededCustomers)
        {
            var isReferenced = await db.Bookings.AnyAsync(x => x.CustomerId == customer.Id, cancellationToken) ||
                await db.Users.AnyAsync(x => x.CustomerId == customer.Id, cancellationToken) ||
                await db.CorporateAccounts.AnyAsync(x => x.CustomerId == customer.Id, cancellationToken);
            if (isReferenced) { customer.IsActive = false; customer.IsBlocked = true; }
            else db.Customers.Remove(customer);
        }
        await db.SaveChangesAsync(cancellationToken);

        var seededCustomers = await db.Customers
            .Where(customer => customerSeeds.Select(seed => seed.CustomerNumber).Contains(customer.CustomerNumber))
            .ToDictionaryAsync(customer => customer.CustomerNumber, cancellationToken);

        const string developmentPassword = "CremsTest!2026";
        var legacyPortalEmails = new Dictionary<string, string>
        {
            ["arieta.vula@customer.example"] = "customer.arieta@crems.local",
            ["rakesh.kumar@customer.example"] = "customer.rakesh@crems.local",
            ["hire@pacificcivil.example"] = "customer.pacificcivil@crems.local",
            ["fleet@coralcoasttours.example"] = "customer.coralcoast@crems.local",
            ["operations@islandevents.example"] = "customer.islandevents@crems.local",
        };
        foreach (var emailChange in legacyPortalEmails)
            await MigrateDevelopmentUserEmailAsync(userManager, emailChange.Key, emailChange.Value);

        var developmentUsers = new[]
        {
            new DevelopmentUserSeed("administrator@crems.local", "Priya Singh", SystemRoles.Administrator, null, null, null),
            new DevelopmentUserSeed("rentals.manager.suva@crems.local", "Laisenia Vakalalabure", SystemRoles.BranchManager, "MOTORS", "SUV", null),
            new DevelopmentUserSeed("rentals.officer.suva@crems.local", "Kelera Waqa", SystemRoles.RentalOfficer, "MOTORS", "SUV", null),
            new DevelopmentUserSeed("rentals.manager.nadi@crems.local", "Rohit Prasad", SystemRoles.BranchManager, "MOTORS", "NAD", null),
            new DevelopmentUserSeed("rentals.officer.nadi@crems.local", "Alipate Tuisese", SystemRoles.RentalOfficer, "MOTORS", "NAD", null),
            new DevelopmentUserSeed("rentals.manager.lautoka@crems.local", "Sanjay Narayan", SystemRoles.BranchManager, "MOTORS", "LAU", null),
            new DevelopmentUserSeed("rentals.officer.lautoka@crems.local", "Salote Waqanivalu", SystemRoles.RentalOfficer, "MOTORS", "LAU", null),
            new DevelopmentUserSeed("rentals.manager.labasa@crems.local", "Mohammed Khan", SystemRoles.BranchManager, "MOTORS", "LAB", null),
            new DevelopmentUserSeed("rentals.officer.labasa@crems.local", "Asenaca Ravuvu", SystemRoles.RentalOfficer, "MOTORS", "LAB", null),
            new DevelopmentUserSeed("carptrac.manager.suva@crems.local", "Mereoni Bale", SystemRoles.BranchManager, "CARPTRAC", "SUV", null),
            new DevelopmentUserSeed("carptrac.officer.suva@crems.local", "Savenaca Driu", SystemRoles.RentalOfficer, "CARPTRAC", "SUV", null),
            new DevelopmentUserSeed("carptrac.manager.lautoka@crems.local", "Anish Chand", SystemRoles.BranchManager, "CARPTRAC", "LAU", null),
            new DevelopmentUserSeed("carptrac.officer.lautoka@crems.local", "Ilisapeci Raloga", SystemRoles.RentalOfficer, "CARPTRAC", "LAU", null),
            new DevelopmentUserSeed("carptrac.manager.labasa@crems.local", "Peni Cakau", SystemRoles.BranchManager, "CARPTRAC", "LAB", null),
            new DevelopmentUserSeed("carptrac.officer.labasa@crems.local", "Shalini Devi", SystemRoles.RentalOfficer, "CARPTRAC", "LAB", null),
            new DevelopmentUserSeed("maintenance.suva@crems.local", "Kelera Waqa", SystemRoles.MaintenanceOfficer, "CARPTRAC", "SUV", null),
            new DevelopmentUserSeed("maintenance.lautoka@crems.local", "Viliame Mataitoga", SystemRoles.MaintenanceOfficer, "CARPTRAC", "LAU", null),
            new DevelopmentUserSeed("maintenance.labasa@crems.local", "Arun Prasad", SystemRoles.MaintenanceOfficer, "CARPTRAC", "LAB", null),
            new DevelopmentUserSeed("customer.arieta@crems.local", "Arieta Vula", SystemRoles.Customer, null, null, "CUS-000001"),
            new DevelopmentUserSeed("customer.rakesh@crems.local", "Rakesh Kumar", SystemRoles.Customer, null, null, "CUS-000004"),
            new DevelopmentUserSeed("customer.pacificcivil@crems.local", "Pacific Civil Works", SystemRoles.Customer, null, null, "CUS-000002"),
            new DevelopmentUserSeed("customer.coralcoast@crems.local", "Coral Coast Tours", SystemRoles.Customer, null, null, "CUS-000007"),
            new DevelopmentUserSeed("customer.islandevents@crems.local", "Island Events & Logistics", SystemRoles.Customer, null, null, "CUS-000003"),
        };
        foreach (var seed in developmentUsers)
        {
            Guid? divisionId = seed.DivisionCode is null ? null : divisions[seed.DivisionCode].Id;
            Guid? branchId = seed.BranchCode is null ? null : branches[seed.BranchCode].Id;
            Guid? customerId = seed.CustomerNumber is null ? null : seededCustomers[seed.CustomerNumber].Id;
            await SeedDevelopmentUserAsync(userManager, seed, developmentPassword, divisionId, branchId, customerId);
        }
        foreach (var obsoleteEmail in new[] { "carptrac.manager.nadi@crems.local", "carptrac.officer.nadi@crems.local" })
        {
            var obsoleteUser = await userManager.FindByEmailAsync(obsoleteEmail);
            if (obsoleteUser is null) continue;
            obsoleteUser.IsActive = false;
            obsoleteUser.AccountStatus = AccountLifecycleStatus.Deactivated;
            obsoleteUser.DeactivatedAt = DateTimeOffset.UtcNow;
            EnsureSucceeded(await userManager.UpdateSecurityStampAsync(obsoleteUser), $"revoke obsolete development user {obsoleteEmail}");
            EnsureSucceeded(await userManager.UpdateAsync(obsoleteUser), $"deactivate obsolete development user {obsoleteEmail}");
        }

        var seededAssets = await db.Assets
            .Where(asset => assetSeeds.Select(seed => seed.AssetNumber).Contains(asset.AssetNumber))
            .ToDictionaryAsync(asset => asset.AssetNumber, cancellationToken);
        var bookingSeeds = new[]
        {
            new BookingSeed("BK-2026-0001", "CUS-000001", "VEH-SUV-1003", BookingStatus.Completed, -45, -40),
            new BookingSeed("BK-2026-0002", "CUS-000002", "EQP-LAU-2002", BookingStatus.Completed, -32, -25),
            new BookingSeed("BK-2026-0003", "CUS-000007", "VEH-NAD-1002", BookingStatus.Completed, -18, -14),
            new BookingSeed("BK-2026-0004", "CUS-000004", "VEH-NAD-1001", BookingStatus.ConvertedToRental, -2, 3),
            new BookingSeed("BK-2026-0005", "CUS-000006", "EQP-LAB-2001", BookingStatus.ConvertedToRental, -5, -1),
            new BookingSeed("BK-2026-0006", "CUS-000005", "VEH-SUV-1001", BookingStatus.Confirmed, 2, 6),
            new BookingSeed("BK-2026-0007", "CUS-000008", "VEH-SUV-1002", BookingStatus.Confirmed, 5, 9),
            new BookingSeed("REQ-2026-0008", "CUS-000009", "VEH-LAU-1002", BookingStatus.Draft, 8, 11),
            new BookingSeed("REQ-2026-0009", "CUS-000003", "EQP-SUV-2002", BookingStatus.Draft, 12, 16),
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
                new PricingRule { Name = "Pacific Civil contracted equipment", CustomerId = seededCustomers["CUS-000002"].Id, AssetType = "Equipment", Period = RatePeriod.Weekly, Rate = 1950m, IncludedUsage = 50m, ExcessUsageRate = 32m, MinimumDuration = 2 });
            db.CorporateAccounts.AddRange(
                new CorporateAccount { CustomerId = seededCustomers["CUS-000002"].Id, LegalName = "Pacific Civil Works Ltd", TaxIdentificationNumber = "TIN-71-45821", CreditLimit = 50000m, PaymentTermsDays = 30, PurchaseOrderRequired = true, BillingContactJson = "{\"name\":\"Accounts Payable\",\"email\":\"accounts@pacificcivil.example\"}", AuthorizedContactsJson = "[]", JobSitesJson = "[\"Vuda Project Yard\",\"Suva Civil Depot\"]", ContractPricingJson = "{}" },
                new CorporateAccount { CustomerId = seededCustomers["CUS-000008"].Id, LegalName = "Viti Freight Services Ltd", TaxIdentificationNumber = "TIN-71-52816", CreditLimit = 35000m, PaymentTermsDays = 14, PurchaseOrderRequired = true, BillingContactJson = "{\"name\":\"Finance Team\",\"email\":\"finance@vitifreight.example\"}", AuthorizedContactsJson = "[]", JobSitesJson = "[\"Walu Bay Depot\"]", ContractPricingJson = "{}" });
            db.InventoryParts.AddRange(
                new InventoryPart { PartNumber = "FLT-OIL-15W40", Name = "Heavy-duty engine oil 15W-40 (20L)", BranchId = branches["SUV"].Id, Supplier = "Carpenters Parts", UnitCost = 186m, QuantityOnHand = 8, QuantityAllocated = 2, ReorderLevel = 4 },
                new InventoryPart { PartNumber = "FLT-FILTER-OIL", Name = "Fleet oil filter", BranchId = branches["SUV"].Id, Supplier = "Carpenters Parts", UnitCost = 38m, QuantityOnHand = 5, QuantityAllocated = 2, ReorderLevel = 5 },
                new InventoryPart { PartNumber = "EQP-BAT-12V", Name = "Heavy equipment battery 12V", BranchId = branches["NAD"].Id, Supplier = "Industrial Battery Fiji", UnitCost = 465m, QuantityOnHand = 1, ReorderLevel = 2 });
            db.CustomerCases.Add(new CustomerCase { CaseNumber = "CASE-2026-0001", CustomerId = seededCustomers["CUS-000006"].Id, BranchId = branches["LAB"].Id, Type = CaseType.Breakdown, Status = CaseStatus.InProgress, Priority = CasePriority.High, Subject = "Compactor pull-start failure at job site", Description = "Customer reported that the unit will not start after normal pre-start checks.", DueAt = DateTimeOffset.UtcNow.AddHours(4) });
            db.ManagementTasks.AddRange(
                new ManagementTask { BranchId = branches["LAB"].Id, Category = TaskCategory.OverdueRental, Priority = TaskPriority.Critical, Title = "Contact customer about overdue rental BK-2026-0005", DueAt = DateTimeOffset.UtcNow.AddHours(1) },
                new ManagementTask { BranchId = branches["NAD"].Id, Category = TaskCategory.LowStock, Priority = TaskPriority.High, Title = "Reorder heavy equipment batteries", DueAt = DateTimeOffset.UtcNow.AddDays(1) },
                new ManagementTask { BranchId = branches["SUV"].Id, Category = TaskCategory.QuoteFollowUp, Priority = TaskPriority.Normal, Title = "Follow up corporate equipment enquiry", DueAt = DateTimeOffset.UtcNow.AddDays(2) });
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!await db.Personnel.AnyAsync(cancellationToken))
        {
            db.Personnel.AddRange(
                new Personnel { EmployeeNumber = "CP-OP-0142", FullName = "Jone Vakalala", BranchId = branches["SUV"].Id, DivisionId = divisions["CARPTRAC"].Id, Type = PersonnelType.Operator, StandardCostRate = 27m, OvertimeCostRate = 40.5m, StandardChargeRate = 42m, OvertimeChargeRate = 63m, Qualifications = [new PersonnelQualification { Name = "Heavy equipment operator competency", CertificateNumber = "FNU-HEO-260142", IssuedOn = new DateOnly(2025, 3, 4), ExpiresOn = new DateOnly(2027, 3, 3), SafetyInduction = true }] },
                new Personnel { EmployeeNumber = "CP-OP-0188", FullName = "Samuela Koroi", BranchId = branches["NAD"].Id, DivisionId = divisions["CARPTRAC"].Id, Type = PersonnelType.Operator, StandardCostRate = 28m, OvertimeCostRate = 42m, StandardChargeRate = 44m, OvertimeChargeRate = 66m, Qualifications = [new PersonnelQualification { Name = "Forklift operator certificate", CertificateNumber = "FJO-2025-188", IssuedOn = new DateOnly(2025, 7, 1), ExpiresOn = new DateOnly(2027, 6, 30), SafetyInduction = true }] },
                new Personnel { EmployeeNumber = "CP-DRV-0097", FullName = "Rajnesh Kumar", BranchId = branches["SUV"].Id, DivisionId = divisions["MOTORS"].Id, Type = PersonnelType.Driver, StandardCostRate = 18m, OvertimeCostRate = 27m, StandardChargeRate = 28m, OvertimeChargeRate = 42m, Qualifications = [new PersonnelQualification { Name = "Group 2 driving licence", CertificateNumber = "DL-2-409781", IssuedOn = new DateOnly(2024, 11, 10), ExpiresOn = new DateOnly(2027, 11, 9), SafetyInduction = true }] },
                new Personnel { EmployeeNumber = "CP-TECH-0064", FullName = "Kelera Waqa", BranchId = branches["SUV"].Id, DivisionId = divisions["CARPTRAC"].Id, Type = PersonnelType.Technician, StandardCostRate = 31m, OvertimeCostRate = 46.5m, StandardChargeRate = 58m, OvertimeChargeRate = 87m });
        }
        if (!await db.Suppliers.AnyAsync(cancellationToken))
            db.Suppliers.AddRange(new Supplier { SupplierNumber = "SUP-0001", Name = "Carpenters Parts", Email = "parts@carpenters.com.fj", Phone = "+679 338 1555", PaymentTermsDays = 30 }, new Supplier { SupplierNumber = "SUP-0002", Name = "Industrial Battery Fiji", Phone = "+679 672 2288", PaymentTermsDays = 14 }, new Supplier { SupplierNumber = "SUP-0003", Name = "Fiji Tyre Services", Phone = "+679 331 2455", PaymentTermsDays = 30 });
        if (!await db.DeliveryZones.AnyAsync(cancellationToken))
            db.DeliveryZones.AddRange(new DeliveryZone { Name = "Suva urban", BranchId = branches["SUV"].Id, BaseCharge = 85m, CostPerKilometre = 2.1m, ChargePerKilometre = 3.4m, FailedDeliveryCharge = 95m }, new DeliveryZone { Name = "Nadi–Lautoka corridor", DivisionId = divisions["CARPTRAC"].Id, BaseCharge = 165m, CostPerKilometre = 2.8m, ChargePerKilometre = 4.5m, FailedDeliveryCharge = 185m });
        if (!await db.BusinessAlertRules.AnyAsync(cancellationToken))
            db.BusinessAlertRules.AddRange(new BusinessAlertRule { Name = "Overdue rental", Category = AlertCategory.OverdueReturn, LeadTimeHours = 0, Priority = TaskPriority.Critical, EmailEnabled = true }, new BusinessAlertRule { Name = "Maintenance due within seven days", Category = AlertCategory.MaintenanceDue, LeadTimeHours = 168, Priority = TaskPriority.High }, new BusinessAlertRule { Name = "Licence expiry within 30 days", Category = AlertCategory.ExpiringLicence, LeadTimeHours = 720, Priority = TaskPriority.High }, new BusinessAlertRule { Name = "Unpaid invoice after terms", Category = AlertCategory.UnpaidInvoice, LeadTimeHours = 720, Priority = TaskPriority.High, EmailEnabled = true });
        if (!await db.AssetLifecycleEvents.AnyAsync(cancellationToken))
            foreach (var asset in seededAssets.Values) db.AssetLifecycleEvents.Add(new AssetLifecycleEvent { AssetId = asset.Id, Type = asset.AcquisitionDate.HasValue ? AssetLifecycleEventType.Commissioned : AssetLifecycleEventType.Available, ToStatus = asset.Status, OccurredAt = asset.AcquisitionDate.HasValue ? new DateTimeOffset(asset.AcquisitionDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(12)) : asset.CreatedAt, MeterReading = asset.CurrentMeterReading, Notes = "Initial lifecycle record created from the asset register.", RecordedByUserId = Guid.Empty, RecordedByName = "CREMS System" });
        await SeedOperationalHistoryAsync(db, seededAssets, cancellationToken);
        await SeedAssetPhotosAsync(db, seededAssets, contentRootPath, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedOperationalHistoryAsync(
        ApplicationDbContext db,
        IReadOnlyDictionary<string, Asset> assets,
        CancellationToken token)
    {
        var bookings = await db.Bookings.Include(x => x.Items).Include(x => x.Customer)
            .Where(x => x.BookingNumber.StartsWith("BK-2026-"))
            .ToDictionaryAsync(x => x.BookingNumber, token);

        if (!await db.AssetInspections.AnyAsync(token))
        {
            var templates = await db.InspectionTemplates.AsNoTracking().ToListAsync(token);
            foreach (var booking in bookings.Values.Where(x => x.Status is BookingStatus.Completed or BookingStatus.ConvertedToRental))
            {
                var item = booking.Items.Single();
                var asset = assets.Values.Single(x => x.Id == item.AssetId);
                var preTemplate = templates.FirstOrDefault(x => x.AssetCategoryId == asset.AssetCategoryId && x.Stage == InspectionStage.PreHire);
                db.AssetInspections.Add(new AssetInspection
                {
                    AssetId = asset.Id, BookingId = booking.Id, TemplateId = preTemplate?.Id,
                    Stage = InspectionStage.PreHire, Outcome = InspectionOutcome.Passed,
                    ResponsesJson = "[{\"section\":\"Condition\",\"result\":\"Passed\"},{\"section\":\"Safety and accessories\",\"result\":\"Passed\"}]",
                    MeterReading = asset.CurrentMeterReading.HasValue ? asset.CurrentMeterReading - (asset.Type == AssetType.Vehicle ? 420 : 18) : null,
                    FuelPercent = asset.Type == AssetType.Vehicle ? 100 : 85,
                    CustomerSignatureName = booking.Customer?.Name ?? "Customer representative",
                    StaffSignatureName = "Rental Operations Team", CompletedByUserId = Guid.Empty,
                    CompletedByName = "Rental Operations Team", CompletedAt = item.StartAt.AddHours(-1),
                    Notes = "Identification, asset condition, issued accessories and operating controls verified before release."
                });
                if (booking.Status != BookingStatus.Completed) continue;
                var postTemplate = templates.FirstOrDefault(x => x.AssetCategoryId == asset.AssetCategoryId && x.Stage == InspectionStage.PostHire);
                db.AssetInspections.Add(new AssetInspection
                {
                    AssetId = asset.Id, BookingId = booking.Id, TemplateId = postTemplate?.Id,
                    Stage = InspectionStage.PostHire, Outcome = booking.BookingNumber == "BK-2026-0002" ? InspectionOutcome.PassedWithNotes : InspectionOutcome.Passed,
                    ResponsesJson = "[{\"section\":\"Return condition\",\"result\":\"Passed\"},{\"section\":\"Meter and fuel\",\"result\":\"Recorded\"}]",
                    MeterReading = asset.CurrentMeterReading, FuelPercent = asset.Type == AssetType.Vehicle ? 72 : 55,
                    CustomerSignatureName = booking.Customer?.Name ?? "Customer representative",
                    StaffSignatureName = "Rental Operations Team", CompletedByUserId = Guid.Empty,
                    CompletedByName = "Rental Operations Team", CompletedAt = item.EndAt.AddMinutes(35),
                    Notes = booking.BookingNumber == "BK-2026-0002"
                        ? "Returned operational. Light bucket wear noted and referred for the next planned service."
                        : "Returned in serviceable condition with no new damage recorded."
                });
            }
        }

        if (!await db.AssetMeterReadings.AnyAsync(token))
        {
            foreach (var asset in assets.Values.Where(x => x.CurrentMeterReading.HasValue).Take(18))
            {
                var current = asset.CurrentMeterReading!.Value;
                var type = asset.Type == AssetType.Vehicle ? MeterType.Odometer : MeterType.EngineHours;
                var increment = asset.Type == AssetType.Vehicle ? 760m : 42m;
                db.AssetMeterReadings.AddRange(
                    new AssetMeterReading { AssetId = asset.Id, Type = type, Unit = asset.MeterUnit ?? (asset.Type == AssetType.Vehicle ? "km" : "hours"), Reading = Math.Max(0, current - increment), FuelPercent = 90, RecordedAt = DateTimeOffset.UtcNow.AddDays(-45), Source = MeterReadingSource.Manual, RecordedByUserId = Guid.Empty },
                    new AssetMeterReading { AssetId = asset.Id, Type = type, Unit = asset.MeterUnit ?? (asset.Type == AssetType.Vehicle ? "km" : "hours"), Reading = Math.Max(0, current - increment / 2), FuelPercent = 70, RecordedAt = DateTimeOffset.UtcNow.AddDays(-20), Source = MeterReadingSource.Maintenance, RecordedByUserId = Guid.Empty },
                    new AssetMeterReading { AssetId = asset.Id, Type = type, Unit = asset.MeterUnit ?? (asset.Type == AssetType.Vehicle ? "km" : "hours"), Reading = current, FuelPercent = 82, RecordedAt = DateTimeOffset.UtcNow.AddDays(-2), Source = MeterReadingSource.Manual, RecordedByUserId = Guid.Empty });
            }
        }

        var maintenanceSeeds = new[]
        {
            new { Number = "MNT-2026-0101", Asset = "VEH-NAD-1002", Type = "Preventive service", Fault = "Scheduled oil, filter, brake and tyre inspection", Status = MaintenanceStatus.Completed, Parts = 185m, Labour = 140m, Transport = 0m, Downtime = 7 },
            new { Number = "MNT-2026-0102", Asset = "EQP-LAU-2002", Type = "Hydraulic inspection", Fault = "Inspect hoses and bucket linkage following return note", Status = MaintenanceStatus.Completed, Parts = 265m, Labour = 310m, Transport = 85m, Downtime = 14 },
            new { Number = "MNT-2026-0103", Asset = "EQP-SUV-2002", Type = "500-hour service", Fault = "Engine oil, filters, mast lubrication and brake adjustment", Status = MaintenanceStatus.Completed, Parts = 420m, Labour = 285m, Transport = 0m, Downtime = 9 },
            new { Number = "MNT-2026-0104", Asset = "VEH-LAU-1002", Type = "Tyre replacement", Fault = "Replace two front tyres and complete wheel alignment", Status = MaintenanceStatus.InProgress, Parts = 690m, Labour = 95m, Transport = 0m, Downtime = 5 },
            new { Number = "MNT-2026-0105", Asset = "EQP-NAD-2002", Type = "Electrical diagnosis", Fault = "Investigate intermittent control-panel warning under load", Status = MaintenanceStatus.WaitingForParts, Parts = 540m, Labour = 220m, Transport = 80m, Downtime = 18 },
        };
        var maintenanceNumbers = await db.MaintenanceJobs.Select(x => x.JobNumber).ToHashSetAsync(token);
        foreach (var seed in maintenanceSeeds.Where(x => !maintenanceNumbers.Contains(x.Number)))
        {
            if (!assets.TryGetValue(seed.Asset, out var asset)) continue;
            var completed = seed.Status == MaintenanceStatus.Completed;
            db.MaintenanceJobs.Add(new MaintenanceJob
            {
                JobNumber = seed.Number, AssetId = asset.Id, BranchId = asset.BranchId,
                Status = seed.Status, ServiceType = seed.Type, FaultDescription = seed.Fault,
                Description = "Workshop record created from the asset service schedule and inspection findings.",
                Priority = completed ? MaintenancePriority.Normal : MaintenancePriority.High,
                AssignedTo = asset.Type == AssetType.Vehicle ? "Carpenters Motors Workshop" : "Carptrac Service Department",
                Supplier = "Carpenters Parts", IsPreventive = seed.Type.Contains("service", StringComparison.OrdinalIgnoreCase),
                EstimatedCost = seed.Parts + seed.Labour + seed.Transport + 75m,
                PartsCost = seed.Parts, LabourCost = seed.Labour, TransportCost = seed.Transport,
                ActualCost = completed ? seed.Parts + seed.Labour + seed.Transport : null,
                PartsUsed = completed ? "Service filters, lubricants and workshop consumables" : null,
                MeterReading = asset.CurrentMeterReading, DowntimeHours = seed.Downtime,
                ReportedAt = DateTimeOffset.UtcNow.AddDays(completed ? -24 : -3),
                CompletedAt = completed ? DateTimeOffset.UtcNow.AddDays(-23) : null,
                NextServiceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(completed ? 6 : 1)),
                NextServiceMeter = asset.CurrentMeterReading + (asset.Type == AssetType.Vehicle ? 10_000 : 500)
            });
            if (!completed) asset.Status = AssetStatus.Maintenance;
        }

        if (!await db.AssetCostEntries.AnyAsync(token))
        {
            var costSeeds = new[]
            {
                new { Asset = "VEH-SUV-1003", Category = AssetCostCategory.Registration, Description = "Annual registration and inspection", Amount = 348m, Supplier = "Land Transport Authority", Reference = "LTA-26-10528" },
                new { Asset = "VEH-NAD-1001", Category = AssetCostCategory.Cleaning, Description = "Post-hire valet and interior sanitisation", Amount = 68m, Supplier = "Nadi Fleet Care", Reference = "NFC-1842" },
                new { Asset = "EQP-LAU-2002", Category = AssetCostCategory.Transport, Description = "Low-bed transport from customer site to Lautoka workshop", Amount = 420m, Supplier = "Western Haulage", Reference = "WH-260811" },
                new { Asset = "EQP-SUV-2002", Category = AssetCostCategory.Fuel, Description = "Diesel replenishment after equipment return", Amount = 176m, Supplier = "Carpenters Service Station", Reference = "FUEL-82614" },
                new { Asset = "VEH-SUV-1001", Category = AssetCostCategory.Insurance, Description = "Monthly comprehensive fleet insurance allocation", Amount = 215m, Supplier = "Fleet insurance allocation", Reference = "INS-2026-08" },
                new { Asset = "SHP-SUV-3001", Category = AssetCostCategory.Cleaning, Description = "Sanitisation consumables and labour", Amount = 42m, Supplier = "Shipping Hire Operations", Reference = "SH-CLEAN-118" },
            };
            foreach (var seed in costSeeds)
            {
                var asset = assets[seed.Asset];
                db.AssetCostEntries.Add(new AssetCostEntry { AssetId = asset.Id, BranchId = asset.BranchId, Category = seed.Category,
                    Description = seed.Description, Amount = seed.Amount, OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-12)),
                    Supplier = seed.Supplier, ReferenceNumber = seed.Reference, RecordedByUserId = Guid.Empty, RecordedByName = "CREMS Finance Seed" });
            }
        }

        foreach (var booking in bookings.Values.Where(x => x.Status == BookingStatus.Completed))
        {
            if (!await db.RentalInvoices.AnyAsync(x => x.BookingId == booking.Id, token))
            {
                var item = booking.Items.Single();
                var days = Math.Max(1, (decimal)Math.Ceiling((item.EndAt - item.StartAt).TotalDays));
                var subtotal = days * item.DailyRate;
                var tax = Math.Round(subtotal * .15m, 2);
                var paid = booking.BookingNumber == "BK-2026-0002" ? Math.Round((subtotal + tax) * .6m, 2) : subtotal + tax;
                db.RentalInvoices.Add(new RentalInvoice
                {
                    InvoiceNumber = booking.BookingNumber.Replace("BK", "INV"), BookingId = booking.Id,
                    LineItemsJson = JsonSerializer.Serialize(new[] { new { description = "Rental hire", quantity = days, unitPrice = item.DailyRate } }),
                    Subtotal = subtotal, TaxAmount = tax, Total = subtotal + tax, AmountPaid = paid,
                    BalanceDue = subtotal + tax - paid, Status = paid >= subtotal + tax ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid,
                    IssuedAt = item.EndAt.AddHours(2), TaxInclusive = false,
                    Lines = [new InvoiceLine { Description = "Rental hire", Quantity = days, UnitPrice = item.DailyRate, TaxRate = 15m }]
                });
                db.RentalPayments.Add(new RentalPayment { BookingId = booking.Id, Type = PaymentType.RentalCharge,
                    Method = booking.Customer?.Type == CustomerType.Business ? PaymentMethod.BankTransfer : PaymentMethod.Card,
                    Amount = paid, ReceiptNumber = booking.BookingNumber.Replace("BK", "RCT"), Status = PaymentStatus.Recorded,
                    Note = "Payment recorded against completed rental.", RecordedByUserId = Guid.Empty, RecordedByName = "CREMS Finance Seed" });
            }
        }

        foreach (var booking in bookings.Values.Where(x => x.Status is BookingStatus.Completed or BookingStatus.ConvertedToRental))
        {
            if (!await db.RentalAgreements.AnyAsync(x => x.BookingId == booking.Id, token))
            {
                var item = booking.Items.Single();
                var asset = assets.Values.Single(x => x.Id == item.AssetId);
                db.RentalAgreements.Add(new RentalAgreement
                {
                    AgreementNumber = booking.BookingNumber.Replace("BK", "RA"), BookingId = booking.Id, BranchId = booking.BranchId,
                    TermsVersion = "CREMS-2026.1", TermsJson = "{\"accepted\":true,\"fuelAndDamageTerms\":true}",
                    CustomerSnapshotJson = JsonSerializer.Serialize(new { booking.Customer?.CustomerNumber, booking.Customer?.Name, booking.Customer?.Email }),
                    AssetSnapshotJson = JsonSerializer.Serialize(new { asset.AssetNumber, asset.Name, asset.RegistrationNumber, asset.SerialNumber }),
                    PricingSnapshotJson = JsonSerializer.Serialize(new { item.DailyRate, booking.TaxRate, booking.DepositRequired }),
                    CustomerSignatureName = booking.Customer?.Name ?? "Customer representative", CustomerSignedAt = item.StartAt.AddMinutes(-25),
                    ApprovedByUserId = Guid.Empty, ApprovedByName = "Rental Operations Team", ApprovedAt = item.StartAt.AddMinutes(-15),
                    Status = booking.Status == BookingStatus.Completed ? AgreementStatus.Completed : AgreementStatus.Active
                });
            }
            if (!await db.AssetLifecycleEvents.AnyAsync(x => x.BookingId == booking.Id, token))
            {
                var item = booking.Items.Single();
                var asset = assets.Values.Single(x => x.Id == item.AssetId);
                db.AssetLifecycleEvents.Add(new AssetLifecycleEvent { AssetId = asset.Id, BookingId = booking.Id, Type = AssetLifecycleEventType.CheckedOut,
                    FromStatus = AssetStatus.Reserved, ToStatus = AssetStatus.Rented, OccurredAt = item.StartAt, MeterReading = asset.CurrentMeterReading,
                    Notes = $"Released under {booking.BookingNumber} after the pre-hire inspection.", RecordedByUserId = Guid.Empty, RecordedByName = "Rental Operations Team" });
                if (booking.Status == BookingStatus.Completed)
                    db.AssetLifecycleEvents.Add(new AssetLifecycleEvent { AssetId = asset.Id, BookingId = booking.Id, Type = AssetLifecycleEventType.ReturnedToService,
                        FromStatus = AssetStatus.Inspection, ToStatus = AssetStatus.Available, OccurredAt = item.EndAt.AddHours(1), MeterReading = asset.CurrentMeterReading,
                        Notes = "Post-hire inspection completed and asset returned to service.", RecordedByUserId = Guid.Empty, RecordedByName = "Rental Operations Team" });
            }
        }
        await db.SaveChangesAsync(token);
    }

    private static async Task SeedAssetPhotosAsync(
        ApplicationDbContext db,
        IReadOnlyDictionary<string, Asset> assets,
        string contentRootPath,
        CancellationToken token)
    {
        var catalogueRoot = Path.GetFullPath(Path.Combine(contentRootPath, "..", "crems-web", "public", "catalog"));
        if (!Directory.Exists(catalogueRoot)) return;
        foreach (var asset in assets.Values)
        {
            var value = $"{asset.Name} {asset.Category}".ToLowerInvariant();
            var sourceNames = value.Contains("portable toilet") ? new[] { "portable-toilet-v2.jpg" }
                : value.Contains("scaffold") ? new[] { "scaffolding-v2.jpg" }
                : value.Contains("bin") ? new[] { "big-bin-v2.jpg" }
                : value.Contains("forklift") || value.Contains("telehandler") ? new[] { "forklift.jpg" }
                : value.Contains("excavator") || value.Contains("backhoe") || value.Contains("loader") ? new[] { "excavator.jpg" }
                : value.Contains("generator") ? new[] { "generator.jpg" }
                : value.Contains("truck") || value.Contains("cargo") ? new[] { "truck.jpg" }
                : value.Contains("urvan") || value.Contains("staria") || value.Contains("coach") ? new[] { "minibus.jpg" }
                : value.Contains("navara") || value.Contains("d-max") ? new[] { "pickup-v2.jpg", "pickup.jpg" }
                : value.Contains("sedan") || value.Contains("i10") ? new[] { "sedan.jpg" }
                : asset.Type == AssetType.Vehicle ? new[] { "suv.jpg" }
                : new[] { "scissor-lift.jpg" };
            var storageRoot = Path.Combine(contentRootPath, "App_Data", "asset-images", asset.Id.ToString("N"));
            Directory.CreateDirectory(storageRoot);
            var urls = new List<string>();
            try
            {
                foreach (var existingUrl in JsonSerializer.Deserialize<List<string>>(asset.PhotoUrlsJson) ?? [])
                    if (existingUrl.StartsWith($"/api/public/assets/{asset.Id}/photos/", StringComparison.Ordinal) && !urls.Contains(existingUrl, StringComparer.Ordinal))
                        urls.Add(existingUrl);
            }
            catch (JsonException) { /* Invalid legacy photo metadata is replaced by files found in storage. */ }
            foreach (var existingFile in Directory.EnumerateFiles(storageRoot))
            {
                var extension = Path.GetExtension(existingFile).ToLowerInvariant();
                if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp")) continue;
                var existingUrl = $"/api/public/assets/{asset.Id}/photos/{Path.GetFileName(existingFile)}";
                if (!urls.Contains(existingUrl, StringComparer.Ordinal)) urls.Add(existingUrl);
            }
            foreach (var sourceName in sourceNames)
            {
                if (urls.Count >= 8) break;
                var source = Path.Combine(catalogueRoot, sourceName);
                if (!File.Exists(source)) continue;
                var extension = Path.GetExtension(sourceName).ToLowerInvariant();
                var storageName = $"seed-{Path.GetFileNameWithoutExtension(sourceName)}{extension}";
                var destination = Path.Combine(storageRoot, storageName);
                if (!File.Exists(destination)) File.Copy(source, destination);
                var url = $"/api/public/assets/{asset.Id}/photos/{storageName}";
                if (!urls.Contains(url, StringComparer.Ordinal)) urls.Add(url);
            }
            if (urls.Count > 0 && !string.Equals(asset.PhotoUrlsJson, JsonSerializer.Serialize(urls), StringComparison.Ordinal))
                asset.PhotoUrlsJson = JsonSerializer.Serialize(urls);
        }
        await db.SaveChangesAsync(token);
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded) return;

        var errors = string.Join("; ", result.Errors.Select(error => error.Description));
        throw new InvalidOperationException($"Unable to {operation}: {errors}");
    }

    private static async Task SeedRolePermissionsAsync(ApplicationDbContext db, CancellationToken token)
    {
        var grants = new Dictionary<string, string[]>
        {
            [SystemRoles.Administrator] = [SystemPermissions.UsersCreate, SystemPermissions.UsersResetPassword, SystemPermissions.UsersManageAccess, SystemPermissions.CustomersManageAccess, SystemPermissions.RentalsApprove, SystemPermissions.MaintenanceComplete, SystemPermissions.ReportsFinancial, SystemPermissions.BranchesConfigure, SystemPermissions.ServicesConfigure, SystemPermissions.AssetCategoriesConfigure, SystemPermissions.BranchCalendarManage, SystemPermissions.PricingConfigure, SystemPermissions.AssetsView, SystemPermissions.AssetsCreate, SystemPermissions.AssetsEdit, SystemPermissions.AssetsTransfer, SystemPermissions.AssetsInspect, SystemPermissions.AssetsRecordMeter, SystemPermissions.AssetsRetire, SystemPermissions.AssetsViewFinancials],
            [SystemRoles.BranchManager] = [SystemPermissions.CustomersManageAccess, SystemPermissions.RentalsApprove, SystemPermissions.MaintenanceComplete, SystemPermissions.ReportsFinancial, SystemPermissions.BranchCalendarManage, SystemPermissions.AssetsView, SystemPermissions.AssetsCreate, SystemPermissions.AssetsEdit, SystemPermissions.AssetsTransfer, SystemPermissions.AssetsInspect, SystemPermissions.AssetsRecordMeter, SystemPermissions.AssetsRetire, SystemPermissions.AssetsViewFinancials],
            [SystemRoles.RentalOfficer] = [SystemPermissions.CustomersManageAccess, SystemPermissions.AssetsView, SystemPermissions.AssetsInspect, SystemPermissions.AssetsRecordMeter],
            [SystemRoles.MaintenanceOfficer] = [SystemPermissions.MaintenanceComplete, SystemPermissions.AssetsView, SystemPermissions.AssetsEdit, SystemPermissions.AssetsInspect, SystemPermissions.AssetsRecordMeter],
        };
        var managedRoles = grants.Keys.ToArray();
        var existingPermissions = await db.RolePermissions
            .Where(x => managedRoles.Contains(x.RoleName))
            .ToListAsync(token);
        var existingKeys = existingPermissions
            .Select(x => $"{x.RoleName}\n{x.Permission}")
            .ToHashSet(StringComparer.Ordinal);
        foreach (var grant in grants)
            foreach (var permission in grant.Value)
                if (existingKeys.Add($"{grant.Key}\n{permission}"))
                    db.RolePermissions.Add(new RolePermission { RoleName = grant.Key, Permission = permission });
        var obsoleteBranchManagerGrant = existingPermissions
            .Where(x => x.RoleName == SystemRoles.BranchManager && x.Permission == SystemPermissions.BranchesConfigure)
            .ToList();
        db.RolePermissions.RemoveRange(obsoleteBranchManagerGrant);
        await db.SaveChangesAsync(token);
    }

    private static async Task DisableLegacyDriverAccessAsync(
        ApplicationDbContext db,
        RoleManager<IdentityRole<Guid>> roleManager,
        UserManager<ApplicationUser> userManager,
        CancellationToken token)
    {
        const string legacyDriverRole = "Driver";
        var legacyPermissions = await db.RolePermissions
            .Where(permission => permission.RoleName == legacyDriverRole)
            .ToListAsync(token);
        db.RolePermissions.RemoveRange(legacyPermissions);

        var role = await roleManager.FindByNameAsync(legacyDriverRole);
        if (role is not null)
        {
            var userIds = await db.UserRoles
                .Where(userRole => userRole.RoleId == role.Id)
                .Select(userRole => userRole.UserId)
                .ToListAsync(token);
            var users = await db.Users.Where(user => userIds.Contains(user.Id)).ToListAsync(token);
            foreach (var user in users.Where(user => user.IsActive || user.AccountStatus != AccountLifecycleStatus.Deactivated))
            {
                user.IsActive = false;
                user.AccountStatus = AccountLifecycleStatus.Deactivated;
                user.DeactivatedAt = DateTimeOffset.UtcNow;
                user.AdminNote = "Portal access removed because drivers are not CREMS users.";
                EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user), $"revoke legacy driver access for {user.Email}");
            }
        }

        await db.SaveChangesAsync(token);
    }

    private static async Task RemoveLegacyFinanceAccessAsync(
        ApplicationDbContext db,
        RoleManager<IdentityRole<Guid>> roleManager,
        UserManager<ApplicationUser> userManager,
        CancellationToken token)
    {
        const string legacyRole = "FinanceOfficer";
        db.RolePermissions.RemoveRange(await db.RolePermissions.Where(permission => permission.RoleName == legacyRole).ToListAsync(token));
        var role = await roleManager.FindByNameAsync(legacyRole);
        if (role is null) { await db.SaveChangesAsync(token); return; }

        var userIds = await db.UserRoles.Where(link => link.RoleId == role.Id).Select(link => link.UserId).ToListAsync(token);
        var financeUsers = await db.Users.Where(user => userIds.Contains(user.Id)).ToListAsync(token);
        foreach (var user in financeUsers)
        {
            var result = await userManager.DeleteAsync(user);
            EnsureSucceeded(result, $"remove legacy finance account {user.Email}");
        }
        EnsureSucceeded(await roleManager.DeleteAsync(role), "remove the legacy Finance Officer role");
        await db.SaveChangesAsync(token);
    }

    private static async Task NormalizeCustomersAsync(ApplicationDbContext db, CancellationToken token)
    {
        var customers = await db.Customers.OrderBy(customer => customer.CreatedAt).ThenBy(customer => customer.Id).ToListAsync(token);
        if (customers.Count == 0) return;

        for (var index = 0; index < customers.Count; index++)
        {
            customers[index].CustomerNumber = $"TMP-{customers[index].Id:N}";
            customers[index].Type = CustomerType.Individual;
        }
        await db.SaveChangesAsync(token);
        for (var index = 0; index < customers.Count; index++) customers[index].CustomerNumber = $"CUS-{index + 1:D6}";
        await db.SaveChangesAsync(token);
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

    private static async Task MigrateDevelopmentUserEmailAsync(
        UserManager<ApplicationUser> userManager, string oldEmail, string newEmail)
    {
        if (await userManager.FindByEmailAsync(newEmail) is not null) return;
        var user = await userManager.FindByEmailAsync(oldEmail);
        if (user is null) return;
        EnsureSucceeded(await userManager.SetEmailAsync(user, newEmail), $"update development email {oldEmail}");
        EnsureSucceeded(await userManager.SetUserNameAsync(user, newEmail), $"update development username {oldEmail}");
        user.EmailConfirmed = true;
        EnsureSucceeded(await userManager.UpdateAsync(user), $"save development email {newEmail}");
    }

    private static IReadOnlyList<InspectionTemplateSeed> CreateCarpentersInspectionTemplates()
    {
        var vehicleSections = new[]
        {
            new ChecklistSectionSeed("Vehicle exterior", ["Body panels", "Front bumper", "Rear bumper", "Windshield", "Windows", "Headlights", "Tail lights", "Tyres", "Spare tyre", "Rims"]),
            new ChecklistSectionSeed("Interior and operation", ["Interior cleanliness", "Seats", "Air conditioning", "Radio / USB", "Registration copy", "Fuel level", "Odometer reading"]),
            new ChecklistSectionSeed("Accessories issued", ["Keys", "Spare key", "Wheel brace", "Jack and tools", "First aid kit", "Fire extinguisher", "GPS"]),
            new ChecklistSectionSeed("Handover evidence", ["Existing scratches and dents recorded", "Vehicle damage diagram completed", "Required photographs attached", "Customer identification verified", "Driving licence verified"]),
        };
        var equipmentSections = new[]
        {
            new ChecklistSectionSeed("Condition and operation", ["Physical condition", "Controls", "Power supply", "Accessories", "Safety labels", "Operational test", "Cables and hoses", "General cleanliness", "Damage check"]),
            new ChecklistSectionSeed("Hire and site controls", ["Site location confirmed", "Project / cost centre recorded", "Delivery or collection details confirmed", "Operator requirement confirmed", "Operator licence or ID verified", "Fuel reading recorded", "Hour meter recorded", "Required photographs attached"]),
        };
        var gensetSections = new[]
        {
            new ChecklistSectionSeed("Engine and electrical", ["Engine condition", "Control panel", "Battery", "Fuel tank", "Cooling system", "Exhaust system", "Alternator", "Meters and gauges", "Circuit breakers", "Cables and connections"]),
            new ChecklistSectionSeed("Safety and accessories", ["Canopy and frame", "Safety decals", "Earth stake provided", "Service log book", "Fire extinguisher", "Leaks detected", "General cleanliness"]),
            new ChecklistSectionSeed("Handover evidence", ["Engine hours recorded", "Fuel level recorded", "Site location confirmed", "Operational test completed", "Required photographs attached", "Faults and existing damage recorded"]),
        };
        var heavyMachineSections = new[]
        {
            new ChecklistSectionSeed("Machine condition", ["Engine", "Hydraulics", "Tracks or tyres", "Cab", "Safety alarms", "Attachments", "Leaks", "Fire extinguisher"]),
            new ChecklistSectionSeed("Hire and site controls", ["Damage diagram completed", "Fuel reading recorded", "Hour meter recorded", "Site and supervisor confirmed", "Operator requirement confirmed", "Operator licence or ID verified", "Safety briefing completed", "Required photographs attached"]),
        };
        var siteHireSections = new[]
        {
            new ChecklistSectionSeed("Asset condition", ["Asset identification verified", "Structure and body condition", "Components and accessories complete", "Cleanliness acceptable", "Safety labels visible", "Existing damage recorded"]),
            new ChecklistSectionSeed("Delivery and collection", ["Customer site confirmed", "Safe placement area confirmed", "Delivery photographs attached", "Customer representative identified", "Delivery or collection time recorded", "Customer acceptance completed"]),
        };

        return
        [
            new("RENTAL_VEHICLE", "Vehicle pre-hire inspection", InspectionStage.PreHire, true, vehicleSections),
            new("RENTAL_VEHICLE", "Vehicle post-hire inspection", InspectionStage.PostHire, true, vehicleSections),
            new("FORKLIFT", "Equipment delivery inspection", InspectionStage.PreHire, true, equipmentSections),
            new("FORKLIFT", "Equipment return inspection", InspectionStage.PostHire, true, equipmentSections),
            new("GENERAL_EQUIPMENT", "Equipment delivery inspection", InspectionStage.PreHire, true, equipmentSections),
            new("GENERAL_EQUIPMENT", "Equipment return inspection", InspectionStage.PostHire, true, equipmentSections),
            new("GENSET", "Genset delivery inspection", InspectionStage.PreHire, true, gensetSections),
            new("GENSET", "Genset return inspection", InspectionStage.PostHire, true, gensetSections),
            new("HEAVY_MACHINE", "Heavy machine delivery inspection", InspectionStage.PreHire, true, heavyMachineSections),
            new("HEAVY_MACHINE", "Heavy machine return inspection", InspectionStage.PostHire, true, heavyMachineSections),
            new("PORTABLE_TOILET", "Portable toilet delivery inspection", InspectionStage.PreHire, true, siteHireSections),
            new("PORTABLE_TOILET", "Portable toilet collection inspection", InspectionStage.PostHire, true, siteHireSections),
            new("SCAFFOLD", "Scaffolding delivery inspection", InspectionStage.PreHire, true, siteHireSections),
            new("SCAFFOLD", "Scaffolding return inspection", InspectionStage.PostHire, true, siteHireSections),
            new("BIG_BIN", "Big-bin delivery inspection", InspectionStage.PreHire, true, siteHireSections),
            new("BIG_BIN", "Big-bin collection inspection", InspectionStage.PostHire, true, siteHireSections),
        ];
    }

    private static string BuildChecklistJson(IReadOnlyList<ChecklistSectionSeed> sections) =>
        JsonSerializer.Serialize(sections.Select(section => new
        {
            section = section.Name,
            items = section.Items.Select((label, index) => new
            {
                code = $"{ToCode(section.Name)}_{index + 1:00}",
                label,
                responseType = "Condition",
                options = new[] { "OK", "Issue", "Not applicable" },
                requiresCommentOnIssue = true,
                allowsPhoto = true,
            }),
        }));

    private static string ToCode(string value) => string.Concat(value.ToUpperInvariant()
        .Select(character => char.IsLetterOrDigit(character) ? character : '_'))
        .Trim('_');

    private static async Task PurgeRemovedDevelopmentDataAsync(ApplicationDbContext db, CancellationToken token)
    {
        var removedCodes = new[] { "HARDWARE", "PROPERTY", "PROPERTIES", "MH" };
        var divisionIds = await db.Divisions.Where(x => removedCodes.Contains(x.Code)).Select(x => x.Id).ToArrayAsync(token);
        if (divisionIds.Length > 0)
        {
            var divisionAssetIds = await db.Assets
                .Where(x => x.DivisionId.HasValue && divisionIds.Contains(x.DivisionId.Value))
                .Select(x => x.Id).ToArrayAsync(token);
            await PurgeAssetsAsync(db, divisionAssetIds, token);

            await db.Users.Where(x => x.DivisionId.HasValue && divisionIds.Contains(x.DivisionId.Value))
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.DivisionId, (Guid?)null), token);
            await db.UserAccessScopes.Where(x => x.DivisionId.HasValue && divisionIds.Contains(x.DivisionId.Value)).ExecuteDeleteAsync(token);
            await db.ApprovalDelegations.Where(x => x.DivisionId.HasValue && divisionIds.Contains(x.DivisionId.Value)).ExecuteDeleteAsync(token);

            var personnelIds = await db.Personnel.Where(x => divisionIds.Contains(x.DivisionId)).Select(x => x.Id).ToArrayAsync(token);
            var assignmentIds = await db.BookingPersonnelAssignments.Where(x => personnelIds.Contains(x.PersonnelId)).Select(x => x.Id).ToArrayAsync(token);
            await db.PersonnelTimesheets.Where(x => assignmentIds.Contains(x.AssignmentId)).ExecuteDeleteAsync(token);
            await db.BookingPersonnelAssignments.Where(x => personnelIds.Contains(x.PersonnelId)).ExecuteDeleteAsync(token);
            await db.PersonnelQualifications.Where(x => personnelIds.Contains(x.PersonnelId)).ExecuteDeleteAsync(token);
            await db.Personnel.Where(x => divisionIds.Contains(x.DivisionId)).ExecuteDeleteAsync(token);

            var quoteIds = await db.SalesQuotes.Where(x => x.DivisionId.HasValue && divisionIds.Contains(x.DivisionId.Value)).Select(x => x.Id).ToArrayAsync(token);
            await db.QuoteRevisions.Where(x => quoteIds.Contains(x.SalesQuoteId)).ExecuteDeleteAsync(token);
            await db.SalesQuotes.Where(x => x.DivisionId.HasValue && divisionIds.Contains(x.DivisionId.Value)).ExecuteDeleteAsync(token);

            var workflowIds = await db.ApprovalWorkflows.Where(x => x.DivisionId.HasValue && divisionIds.Contains(x.DivisionId.Value)).Select(x => x.Id).ToArrayAsync(token);
            await db.ApprovalRequests.Where(x => x.WorkflowId.HasValue && workflowIds.Contains(x.WorkflowId.Value))
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.WorkflowId, (Guid?)null), token);
            await db.ApprovalWorkflowStages.Where(x => workflowIds.Contains(x.WorkflowId)).ExecuteDeleteAsync(token);
            await db.ApprovalWorkflows.Where(x => x.DivisionId.HasValue && divisionIds.Contains(x.DivisionId.Value)).ExecuteDeleteAsync(token);

            var alertRuleIds = await db.BusinessAlertRules.Where(x => x.DivisionId.HasValue && divisionIds.Contains(x.DivisionId.Value)).Select(x => x.Id).ToArrayAsync(token);
            await db.BusinessAlerts.Where(x =>
                x.DivisionId.HasValue && divisionIds.Contains(x.DivisionId.Value) ||
                x.RuleId.HasValue && alertRuleIds.Contains(x.RuleId.Value)).ExecuteDeleteAsync(token);
            await db.BusinessAlertRules.Where(x => x.DivisionId.HasValue && divisionIds.Contains(x.DivisionId.Value)).ExecuteDeleteAsync(token);
            await db.DeliveryZones.Where(x => x.DivisionId.HasValue && divisionIds.Contains(x.DivisionId.Value)).ExecuteDeleteAsync(token);
            await db.PricingRules.Where(x => x.DivisionId.HasValue && divisionIds.Contains(x.DivisionId.Value)).ExecuteDeleteAsync(token);

            var categoryIds = await db.AssetCategories.Where(x => divisionIds.Contains(x.DivisionId)).Select(x => x.Id).ToArrayAsync(token);
            var definitionIds = await db.AssetAttributeDefinitions.Where(x => categoryIds.Contains(x.AssetCategoryId)).Select(x => x.Id).ToArrayAsync(token);
            var templateIds = await db.InspectionTemplates.Where(x => categoryIds.Contains(x.AssetCategoryId)).Select(x => x.Id).ToArrayAsync(token);
            await db.AssetInspections.Where(x => x.TemplateId.HasValue && templateIds.Contains(x.TemplateId.Value)).ExecuteDeleteAsync(token);
            await db.AssetAttributeValues.Where(x => definitionIds.Contains(x.AttributeDefinitionId)).ExecuteDeleteAsync(token);
            await db.InspectionTemplates.Where(x => categoryIds.Contains(x.AssetCategoryId)).ExecuteDeleteAsync(token);
            await db.AssetAttributeDefinitions.Where(x => categoryIds.Contains(x.AssetCategoryId)).ExecuteDeleteAsync(token);
            await db.AssetCategories.Where(x => divisionIds.Contains(x.DivisionId)).ExecuteDeleteAsync(token);

            var chargeIds = await db.ChargeDefinitions.Where(x => divisionIds.Contains(x.DivisionId)).Select(x => x.Id).ToArrayAsync(token);
            await db.BookingCharges.Where(x => x.ChargeDefinitionId.HasValue && chargeIds.Contains(x.ChargeDefinitionId.Value)).ExecuteDeleteAsync(token);
            await db.BranchDivisionServices.Where(x => divisionIds.Contains(x.DivisionId)).ExecuteDeleteAsync(token);
            await db.BranchDivisions.Where(x => divisionIds.Contains(x.DivisionId)).ExecuteDeleteAsync(token);
            await db.ChargeDefinitions.Where(x => divisionIds.Contains(x.DivisionId)).ExecuteDeleteAsync(token);
            await db.ServiceOfferings.Where(x => divisionIds.Contains(x.DivisionId)).ExecuteDeleteAsync(token);
            await db.AuditEvents.Where(x => divisionIds.Contains(x.EntityId)).ExecuteDeleteAsync(token);
            await db.DocumentRecords.Where(x => divisionIds.Contains(x.EntityId)).ExecuteDeleteAsync(token);
            await db.Divisions.Where(x => removedCodes.Contains(x.Code)).ExecuteDeleteAsync(token);
        }

        var shippingId = await db.Divisions.Where(x => x.Code == "SHIPPING").Select(x => (Guid?)x.Id).SingleOrDefaultAsync(token);
        if (shippingId.HasValue)
        {
            var allowedServiceCodes = new[] { "PORTABLE_TOILET_HIRE", "SCAFFOLDING_HIRE", "BIG_BIN_HIRE" };
            var obsoleteServiceIds = await db.ServiceOfferings
                .Where(x => x.DivisionId == shippingId.Value && !allowedServiceCodes.Contains(x.Code))
                .Select(x => x.Id).ToArrayAsync(token);
            if (obsoleteServiceIds.Length > 0)
            {
                var obsoleteAssetIds = await db.Assets.Where(x => x.ServiceOfferingId.HasValue && obsoleteServiceIds.Contains(x.ServiceOfferingId.Value))
                    .Select(x => x.Id).ToArrayAsync(token);
                await PurgeAssetsAsync(db, obsoleteAssetIds, token);

                var obsoleteCategoryIds = await db.AssetCategories
                    .Where(x => x.ServiceOfferingId.HasValue && obsoleteServiceIds.Contains(x.ServiceOfferingId.Value))
                    .Select(x => x.Id).ToArrayAsync(token);
                var obsoleteDefinitionIds = await db.AssetAttributeDefinitions.Where(x => obsoleteCategoryIds.Contains(x.AssetCategoryId))
                    .Select(x => x.Id).ToArrayAsync(token);
                var obsoleteTemplateIds = await db.InspectionTemplates.Where(x => obsoleteCategoryIds.Contains(x.AssetCategoryId))
                    .Select(x => x.Id).ToArrayAsync(token);
                await db.AssetInspections.Where(x => x.TemplateId.HasValue && obsoleteTemplateIds.Contains(x.TemplateId.Value)).ExecuteDeleteAsync(token);
                await db.AssetAttributeValues.Where(x => obsoleteDefinitionIds.Contains(x.AttributeDefinitionId)).ExecuteDeleteAsync(token);
                await db.InspectionTemplates.Where(x => obsoleteCategoryIds.Contains(x.AssetCategoryId)).ExecuteDeleteAsync(token);
                await db.AssetAttributeDefinitions.Where(x => obsoleteCategoryIds.Contains(x.AssetCategoryId)).ExecuteDeleteAsync(token);
                await db.PricingRules.Where(x =>
                    x.ServiceOfferingId.HasValue && obsoleteServiceIds.Contains(x.ServiceOfferingId.Value) ||
                    x.AssetCategoryId.HasValue && obsoleteCategoryIds.Contains(x.AssetCategoryId.Value)).ExecuteDeleteAsync(token);
                await db.AssetCategories.Where(x => obsoleteCategoryIds.Contains(x.Id)).ExecuteDeleteAsync(token);

                var obsoleteChargeIds = await db.ChargeDefinitions
                    .Where(x => x.ServiceOfferingId.HasValue && obsoleteServiceIds.Contains(x.ServiceOfferingId.Value))
                    .Select(x => x.Id).ToArrayAsync(token);
                await db.BookingCharges.Where(x => x.ChargeDefinitionId.HasValue && obsoleteChargeIds.Contains(x.ChargeDefinitionId.Value)).ExecuteDeleteAsync(token);
                await db.PricingRules.Where(x => x.ChargeDefinitionId.HasValue && obsoleteChargeIds.Contains(x.ChargeDefinitionId.Value)).ExecuteDeleteAsync(token);
                await db.ChargeDefinitions.Where(x => x.ServiceOfferingId.HasValue && obsoleteServiceIds.Contains(x.ServiceOfferingId.Value)).ExecuteDeleteAsync(token);
                await db.BranchDivisionServices.Where(x => obsoleteServiceIds.Contains(x.ServiceOfferingId)).ExecuteDeleteAsync(token);
                await db.ServiceOfferings.Where(x => obsoleteServiceIds.Contains(x.Id)).ExecuteDeleteAsync(token);
            }
        }

        await PurgeRetiredAssetsAsync(db, token);
    }

    private static async Task PurgeRetiredAssetsAsync(ApplicationDbContext db, CancellationToken token)
    {
        var assetIds = await db.Assets.Where(x => x.Status == AssetStatus.Retired).Select(x => x.Id).ToArrayAsync(token);
        await PurgeAssetsAsync(db, assetIds, token);
    }

    private static async Task PurgeAssetsAsync(ApplicationDbContext db, Guid[] assetIds, CancellationToken token)
    {
        if (assetIds.Length == 0) return;
        var maintenanceJobIds = await db.MaintenanceJobs.Where(x => assetIds.Contains(x.AssetId)).Select(x => x.Id).ToArrayAsync(token);
        await db.MaintenancePartUsages.Where(x => maintenanceJobIds.Contains(x.MaintenanceJobId)).ExecuteDeleteAsync(token);
        await db.AssetAttributeValues.Where(x => assetIds.Contains(x.AssetId)).ExecuteDeleteAsync(token);
        await db.AssetCostEntries.Where(x => assetIds.Contains(x.AssetId)).ExecuteDeleteAsync(token);
        await db.AssetInspections.Where(x => assetIds.Contains(x.AssetId)).ExecuteDeleteAsync(token);
        await db.AssetLifecycleEvents.Where(x => assetIds.Contains(x.AssetId)).ExecuteDeleteAsync(token);
        await db.AssetMeterReadings.Where(x => assetIds.Contains(x.AssetId)).ExecuteDeleteAsync(token);
        await db.AssetTransfers.Where(x => assetIds.Contains(x.AssetId)).ExecuteDeleteAsync(token);
        await db.TelematicsSnapshots.Where(x => assetIds.Contains(x.AssetId)).ExecuteDeleteAsync(token);
        await db.PricingRules.Where(x => x.AssetId.HasValue && assetIds.Contains(x.AssetId.Value)).ExecuteDeleteAsync(token);
        await db.BookingCharges.Where(x => x.AssetId.HasValue && assetIds.Contains(x.AssetId.Value)).ExecuteDeleteAsync(token);
        await db.BookingItems.Where(x => assetIds.Contains(x.AssetId)).ExecuteDeleteAsync(token);
        await db.MaintenanceJobs.Where(x => assetIds.Contains(x.AssetId)).ExecuteDeleteAsync(token);
        await db.BusinessAlerts.Where(x => x.EntityId.HasValue && assetIds.Contains(x.EntityId.Value)).ExecuteDeleteAsync(token);
        await db.ManagementTasks.Where(x => x.SourceEntityId.HasValue && assetIds.Contains(x.SourceEntityId.Value)).ExecuteDeleteAsync(token);
        await db.ApprovalRequests.Where(x => assetIds.Contains(x.EntityId)).ExecuteDeleteAsync(token);
        await db.AuditEvents.Where(x => assetIds.Contains(x.EntityId)).ExecuteDeleteAsync(token);
        await db.DocumentRecords.Where(x => assetIds.Contains(x.EntityId)).ExecuteDeleteAsync(token);
        await db.Assets.Where(x => assetIds.Contains(x.Id)).ExecuteDeleteAsync(token);
    }

    private sealed record BranchSeed(string Code, string Name, string Address, string Phone);
    private sealed record AssetCategorySeed(string Code, string Name, string DivisionCode, string ServiceCode,
        string MeterType, PersonnelRequirement PersonnelRequirement, IReadOnlyList<AssetAttributeSeed> Attributes);
    private sealed record AssetAttributeSeed(string Code, string Name, AttributeDataType DataType, string? Unit,
        bool IsRequired, bool IsSearchable, bool IsCustomerVisible = true, string OptionsJson = "[]");
    private sealed record InspectionTemplateSeed(string CategoryCode, string Name, InspectionStage Stage,
        bool RequiresCustomerSignature, IReadOnlyList<ChecklistSectionSeed> Sections);
    private sealed record ChecklistSectionSeed(string Name, IReadOnlyList<string> Items);
    private sealed record DivisionSeed(string Code, string Name, string Description, DivisionCapabilities Capabilities, IReadOnlyList<ServiceSeed> Services);
    private sealed record ServiceSeed(string Code, string Name, ServiceOfferingType Type, PersonnelRequirement PersonnelRequirement,
        bool IsBookableOnline, bool RequiresQuote, ChargeUnit DefaultHireUnit = ChargeUnit.Day, bool RequiresDelivery = false);
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
