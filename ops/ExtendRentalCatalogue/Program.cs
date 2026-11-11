using System.Data;
using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Rentals;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var config = new ConfigurationBuilder().SetBasePath(Path.Combine(root, "src/CREMS.Api"))
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets("crems-cs400-development").AddEnvironmentVariables().Build();
var connection = config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Missing database connection.");
var target = new SqlConnectionStringBuilder(connection);
if (!string.Equals(target.InitialCatalog, "Crems", StringComparison.OrdinalIgnoreCase) ||
    !(target.DataSource.StartsWith("localhost,", StringComparison.OrdinalIgnoreCase) || target.DataSource == "localhost" ||
      target.DataSource.StartsWith("127.0.0.1,", StringComparison.Ordinal) || target.DataSource == "127.0.0.1"))
    throw new InvalidOperationException("This utility only targets the local Crems database.");
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
if (args.Contains("--apply"))
{
    using var suppression = db.SuppressNotifications();
    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(12)).DateTime);
    var motors = await db.Divisions.SingleAsync(x => x.Code == "MOTORS");
    var vehicleService = await db.ServiceOfferings.FirstAsync(x => x.DivisionId == motors.Id && x.Type == ServiceOfferingType.VehicleRental && x.IsActive);
    var branches = await db.Branches.Where(x => x.IsActive && x.IsPublic).ToListAsync();
    var suva = branches.Single(x => x.Code == "SUV"); var nadi = branches.FirstOrDefault(x => x.Code == "NAD") ?? suva;
    var categories = await db.AssetCategories.Include(x => x.AttributeDefinitions).ToListAsync();
    var assets = await db.Assets.Select(x => x.AssetNumber).ToListAsync(); var assetNumbers = assets.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var templates = await db.InspectionTemplates.ToListAsync();
    var added = new List<string>();
    async Task Link(Division division, ServiceOffering service, Branch branch)
    {
        var link = await db.BranchDivisions.FirstOrDefaultAsync(x => x.BranchId == branch.Id && x.DivisionId == division.Id);
        if (link == null) { link = new BranchDivision { BranchId = branch.Id, DivisionId = division.Id }; db.BranchDivisions.Add(link); }
        link.IsActive = true; link.AcceptsBookings = true;
        var serviceLink = await db.BranchDivisionServices.FirstOrDefaultAsync(x => x.BranchId == branch.Id && x.DivisionId == division.Id && x.ServiceOfferingId == service.Id);
        if (serviceLink == null) { serviceLink = new BranchDivisionService { BranchId = branch.Id, DivisionId = division.Id, ServiceOfferingId = service.Id }; db.BranchDivisionServices.Add(serviceLink); }
        serviceLink.IsActive = true; serviceLink.IsBookable = true;
    }
    AssetCategory Category(Division division, ServiceOffering service, string code, string name, AssetType type)
    {
        var found = categories.FirstOrDefault(x => x.DivisionId == division.Id && (x.Code == code || x.Name == name));
        if (found != null) return found;
        var item = new AssetCategory { DivisionId = division.Id, ServiceOfferingId = service.Id, Code = code, Name = name,
            DefaultAssetType = type, DefaultMeterType = type == AssetType.LightEquipment ? "hours" : "km", PersonnelRequirement = PersonnelRequirement.None };
        db.AssetCategories.Add(item); categories.Add(item); added.Add("Category: " + name); return item;
    }
    AssetAttributeDefinition Attribute(AssetCategory category, string code, string name, AttributeDataType type, string? unit = null)
    {
        var existing = category.AttributeDefinitions.FirstOrDefault(x => x.Code == code);
        if (existing != null) return existing;
        var item = new AssetAttributeDefinition { AssetCategoryId = category.Id, Code = code, Name = name, DataType = type, Unit = unit,
            IsSearchable = true, IsCustomerVisible = true, IsReportable = true, DisplayOrder = category.AttributeDefinitions.Count };
        category.AttributeDefinitions.Add(item); db.AssetAttributeDefinitions.Add(item); return item;
    }
    void Checklists(AssetCategory category, bool vehicle)
    {
        var items = vehicle ? new[] { "Asset number and keys match the booking", "Body, glass and existing damage checked", "Tyres, lights and indicators operational", "Cabin, seatbelts and safety equipment checked", "Odometer and fuel level recorded", "Spare tyre, jack and supplied accessories accounted for" }
            : new[] { "Asset tag and serial identification checked", "Lens and casing clean and undamaged", "Image, focus and HDMI input tested", "Power cable, HDMI cable and remote included", "Lamp hours recorded and ventilation clear", "Carry case and supplied accessories accounted for" };
        var json = JsonSerializer.Serialize(new[] { new { section = vehicle ? "Vehicle condition" : "Projector condition", items = items.Select(label => new { label }) } });
        foreach (var stage in new[] { InspectionStage.PreHire, InspectionStage.PostHire })
            if (!templates.Any(x => x.AssetCategoryId == category.Id && x.Stage == stage && x.IsActive))
            {
                var item = new InspectionTemplate { AssetCategoryId = category.Id, Stage = stage, Name = category.Name + (stage == InspectionStage.PreHire ? " pickup checklist" : " return checklist"), ChecklistJson = json, RequiresCustomerSignature = true };
                templates.Add(item); db.InspectionTemplates.Add(item);
            }
    }
    void AddAsset(string number, string name, string manufacturer, string model, int year, decimal rate, decimal cost,
        decimal reading, Branch branch, Division division, ServiceOffering service, AssetCategory category, string image,
        params (AssetAttributeDefinition Definition, string Value)[] values)
    {
        if (!assetNumbers.Add(number)) return;
        var asset = new Asset { AssetNumber = number, Name = name, Manufacturer = manufacturer, Model = model, ModelYear = year,
            Type = category.DefaultAssetType ?? AssetType.PassengerVehicle, DivisionId = division.Id, ServiceOfferingId = service.Id,
            AssetCategoryId = category.Id, Category = category.Name, BranchId = branch.Id, Status = AssetStatus.Available, IsActive = true,
            DailyRate = rate, HireUnit = ChargeUnit.Day, InheritBond = true, CurrentMeterReading = reading,
            MeterUnit = category.DefaultMeterType, AcquisitionDate = today.AddMonths(-18), AcquisitionCost = cost,
            CurrentBookValue = decimal.Round(cost * 0.8m, 2), OwnershipType = "Owned", CurrentLocation = branch.Name + " rental depot",
            NextServiceDate = today.AddDays(30), InsuranceExpiry = category.DefaultAssetType == AssetType.LightEquipment ? null : today.AddMonths(6),
            PhotoUrlsJson = JsonSerializer.Serialize(new[] { image }), PersonnelOverride = PersonnelRequirement.None,
            FulfilmentMode = AssetFulfilmentMode.PickupOrDelivery, TurnaroundMinutes = category.DefaultAssetType == AssetType.LightEquipment ? 30 : 60 };
        db.Assets.Add(asset);
        foreach (var (definition, value) in values) db.AssetAttributeValues.Add(new AssetAttributeValue { AssetId = asset.Id, AttributeDefinitionId = definition.Id, Value = value });
        added.Add($"Asset: {number} - {name} ({branch.Name}), FJD {rate}/day");
    }
    var catalogue = new[] {
        ("SEDAN", "Sedan", AssetType.PassengerVehicle), ("HATCHBACK", "Hatchback", AssetType.PassengerVehicle),
        ("SUV", "SUV", AssetType.PassengerVehicle), ("PICKUP", "Pickup", AssetType.CommercialVehicle), ("MINIVAN", "Minivan", AssetType.PassengerVehicle) };
    foreach (var (code, label, type) in catalogue)
    {
        var category = Category(motors, vehicleService, code, label, type); Checklists(category, true);
        var seats = Attribute(category, "SEATS", "Seats", AttributeDataType.Integer);
        var transmission = Attribute(category, "TRANSMISSION", "Transmission", AttributeDataType.Text);
        var air = Attribute(category, "AIR_CONDITIONING", "Air conditioning", AttributeDataType.Boolean);
        if (code == "SEDAN") {
            AddAsset("CM-SED-001", "Toyota Corolla Sedan", "Toyota", "Corolla", 2022, 135, 42000, 38420, suva, motors, vehicleService, category, "/catalog/sedan.jpg", (seats,"5"),(transmission,"Automatic"),(air,"true"));
            AddAsset("CM-SED-002", "Toyota Corolla Sedan", "Toyota", "Corolla", 2023, 145, 46500, 26750, nadi, motors, vehicleService, category, "/catalog/sedan.jpg", (seats,"5"),(transmission,"Automatic"),(air,"true"));
        } else if (code == "HATCHBACK") {
            AddAsset("CM-HAT-001", "Toyota Yaris Hatchback", "Toyota", "Yaris", 2022, 105, 31000, 31280, suva, motors, vehicleService, category, "/catalog/hatchback-v2.png", (seats,"5"),(transmission,"Automatic"),(air,"true"));
            AddAsset("CM-HAT-002", "Toyota Yaris Hatchback", "Toyota", "Yaris", 2023, 115, 35000, 22460, nadi, motors, vehicleService, category, "/catalog/hatchback-v2.png", (seats,"5"),(transmission,"Automatic"),(air,"true"));
        } else if (code == "SUV") {
            AddAsset("CM-SUV-001", "Toyota RAV4 SUV", "Toyota", "RAV4", 2022, 195, 68000, 35600, suva, motors, vehicleService, category, "/catalog/suv.jpg", (seats,"5"),(transmission,"Automatic"),(air,"true"));
            AddAsset("CM-SUV-002", "Toyota RAV4 SUV", "Toyota", "RAV4", 2023, 210, 74500, 24800, nadi, motors, vehicleService, category, "/catalog/suv.jpg", (seats,"5"),(transmission,"Automatic"),(air,"true"));
        } else if (code == "PICKUP") {
            AddAsset("CM-PUP-001", "Toyota Hilux Double Cab", "Toyota", "Hilux", 2023, 225, 78000, 32870, suva, motors, vehicleService, category, "/catalog/pickup-v2.jpg", (seats,"5"),(transmission,"Automatic"),(air,"true"));
        } else {
            AddAsset("CM-VAN-001", "Toyota Hiace Minivan", "Toyota", "Hiace", 2022, 245, 82000, 42750, nadi, motors, vehicleService, category, "/catalog/minibus.jpg", (seats,"12"),(transmission,"Automatic"),(air,"true"));
        }
    }
    await Link(motors, vehicleService, suva); if (nadi.Id != suva.Id) await Link(motors, vehicleService, nadi);
    var events = await db.Divisions.FirstOrDefaultAsync(x => x.Code == "EVENTS" || x.Name == "Carpenters Events");
    if (events == null) {
        events = new Division { Code = "EVENTS", Name = "Carpenters Events", Description = "Presentation and audiovisual equipment hire for meetings, training and functions.",
            Capabilities = DivisionCapabilities.Rental | DivisionCapabilities.Maintenance, DefaultCurrency = motors.DefaultCurrency,
            DefaultTaxRate = motors.DefaultTaxRate, DefaultBondAmount = 150,
            BrandingJson = "{\"primaryColor\":\"#15140f\",\"accentColor\":\"#ffed00\"}",
            DefaultRentalTerms = "Return the projector with its remote, power lead, HDMI cable and carry case. Keep the unit dry, allow it to cool before packing, and report damage promptly.",
            CustomerBookingConfigurationJson = new DivisionBookingFlow(QuoteMode: "Automatic", BusinessCustomersRequireQuote: false).ToJson() };
        db.Divisions.Add(events); added.Add("Division: Carpenters Events");
    }
    var projectorService = await db.ServiceOfferings.FirstOrDefaultAsync(x => x.DivisionId == events.Id && x.Code == "PROJECTOR_HIRING");
    if (projectorService == null) {
        projectorService = new ServiceOffering { DivisionId = events.Id, Code = "PROJECTOR_HIRING", Name = "Projector hiring", Type = ServiceOfferingType.EquipmentHire,
            Description = "Portable projector hire with remote, power lead, HDMI cable and carry case.", IsBookableOnline = true,
            DefaultHireUnit = ChargeUnit.Day, InheritBond = true, PersonnelRequirement = PersonnelRequirement.None,
            MeterType = "hours", InspectionRequirementsJson = "{\"tracksFuel\":false,\"tracksMeter\":true}" };
        db.ServiceOfferings.Add(projectorService); added.Add("Service: Projector hiring");
    }
    var projector = Category(events, projectorService, "PROJECTOR", "Projector", AssetType.LightEquipment); Checklists(projector, false);
    var brightness = Attribute(projector, "BRIGHTNESS", "Brightness", AttributeDataType.Integer, "lumens");
    var resolution = Attribute(projector, "RESOLUTION", "Native resolution", AttributeDataType.Text);
    var hdmi = Attribute(projector, "HDMI_INPUT", "HDMI input", AttributeDataType.Boolean);
    var accessories = Attribute(projector, "ACCESSORIES", "Included accessories", AttributeDataType.Text);
    AddAsset("CE-PJ-001", "Epson EB-X49 Projector", "Epson", "EB-X49", 2023, 85, 1850, 186, suva, events, projectorService, projector,
        "/catalog/projector-eb-x49.jpg", (brightness,"3600"),(resolution,"1024 x 768 (XGA)"),(hdmi,"true"),(accessories,"Remote control, power lead, HDMI cable and carry case"));
    await Link(events, projectorService, suva);
    await db.SaveChangesAsync(); await transaction.CommitAsync();
    Console.WriteLine(added.Count == 0 ? "Catalogue already configured. No duplicate records added." : string.Join(Environment.NewLine, added));
    Console.WriteLine("Catalogue rates are editable configuration amounts, exclusive of the configured VAT. No bookings, customer records or payments were added.");
}
if (args.Contains("--verify"))
{
    var tags = new[] { "CM-SED-001", "CM-SED-002", "CM-HAT-001", "CM-HAT-002", "CM-SUV-001", "CM-SUV-002", "CM-PUP-001", "CM-VAN-001", "CE-PJ-001" };
    var configured = await db.Assets.AsNoTracking().Include(x => x.AssetCategory).Include(x => x.Division).Include(x => x.ServiceOffering)
        .Where(x => tags.Contains(x.AssetNumber)).ToListAsync();
    if (configured.Count != tags.Length) throw new InvalidOperationException("Expected nine catalogue assets.");
    foreach (var asset in configured)
    {
        if (!asset.IsActive || asset.DailyRate <= 0 || asset.AssetCategory is null || asset.ServiceOffering is null || asset.Division is null || asset.AssetCategory.DivisionId != asset.DivisionId ||
            asset.AssetCategory.ServiceOfferingId != asset.ServiceOfferingId || asset.ServiceOffering.DivisionId != asset.DivisionId ||
            new[] { asset.Name, asset.AssetNumber, asset.AssetCategory.Name, asset.ServiceOffering.Name, asset.Division!.Name }.Any(value => value.Contains("demo", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Invalid catalogue configuration for " + asset.AssetNumber);
        if (!await db.BranchDivisionServices.AnyAsync(x => x.BranchId == asset.BranchId && x.DivisionId == asset.DivisionId && x.ServiceOfferingId == asset.ServiceOfferingId && x.IsActive && x.IsBookable))
            throw new InvalidOperationException("Missing bookable branch link for " + asset.AssetNumber);
        if (await db.InspectionTemplates.CountAsync(x => x.AssetCategoryId == asset.AssetCategoryId && x.IsActive && (x.Stage == InspectionStage.PreHire || x.Stage == InspectionStage.PostHire)) < 2)
            throw new InvalidOperationException("Missing pickup/return templates for " + asset.AssetNumber);
    }
    var projector = configured.Single(x => x.AssetNumber == "CE-PJ-001");
    if (AssetCategoryPolicy.TracksFuel(projector) || AssetCategoryPolicy.RequiresDrivingLicence(projector, false) || !AssetCategoryPolicy.TracksMeter(projector))
        throw new InvalidOperationException("Projector has inappropriate inspection rules.");
    Console.WriteLine("Verified nine active assets, category/service/division consistency, bookable branch links, pickup/return checklists and projector-specific rules.");
    return;
}
foreach (var division in await db.Divisions.Include(x => x.ServiceOfferings).OrderBy(x => x.Name).ToListAsync())
{
    Console.WriteLine($"{division.Name} [{division.Code}] {division.Id}");
    foreach (var service in division.ServiceOfferings) Console.WriteLine($"  Service: {service.Name} [{service.Code}] type={service.Type} active={service.IsActive}");
    foreach (var category in await db.AssetCategories.Where(x => x.DivisionId == division.Id).ToListAsync())
        Console.WriteLine($"  Category: {category.Name} [{category.Code}], service={category.ServiceOfferingId}, active={category.IsActive}, assets={await db.Assets.CountAsync(x => x.AssetCategoryId == category.Id)}");
}
foreach (var branch in await db.Branches.ToListAsync()) Console.WriteLine($"Branch: {branch.Name} [{branch.Code}], active={branch.IsActive}, public={branch.IsPublic}, {branch.Id}");
