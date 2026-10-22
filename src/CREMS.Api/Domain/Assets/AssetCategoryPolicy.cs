using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Rentals;

namespace CREMS.Api.Domain.Assets;

/// <summary>Operational behaviour derived from the configured asset category.</summary>
public static class AssetCategoryPolicy
{
    public static bool IsVehicle(AssetType type) => type is AssetType.Vehicle or AssetType.PassengerVehicle or AssetType.CommercialVehicle;
    public static bool IsEquipment(AssetType type) => !IsVehicle(type) && type != AssetType.PropertyUnit;
    public static string Code(Asset asset) => asset.Type == AssetType.PropertyUnit ? "PROPERTY_UNIT" : asset.AssetCategory?.Code?.ToUpperInvariant() ??
        (IsVehicle(asset.Type) ? "RENTAL_VEHICLE" : "GENERAL_EQUIPMENT");

    public static PersonnelRequirement Personnel(Asset asset) =>
        asset.Type == AssetType.PropertyUnit ? PersonnelRequirement.None :
        asset.PersonnelOverride ?? DivisionBookingFlow.For(asset.Division).PersonnelRequirement;

    public static bool AllowsPersonnel(Asset asset) => Personnel(asset) != PersonnelRequirement.None;
    public static decimal Bond(Asset asset) => !asset.InheritBond ? asset.DefaultBondAmount :
        asset.Division?.DefaultBondAmount ?? 0m;
    public static bool RequiresPersonnel(Asset asset) => Personnel(asset) == PersonnelRequirement.Required;
    public static bool RequiresDrivingLicence(Asset asset, bool professionalPersonnelProvided) =>
        IsVehicle(asset.Type) && !professionalPersonnelProvided;
    public static bool TracksFuel(Asset asset) => asset.Type != AssetType.PropertyUnit && Code(asset) is
        "RENTAL_VEHICLE" or "FORKLIFT" or "GENSET" or "HEAVY_MACHINE" or "GENERAL_EQUIPMENT";
    public static bool TracksMeter(Asset asset) => asset.Type != AssetType.PropertyUnit && Code(asset) is not
        ("PORTABLE_TOILET" or "SCAFFOLD" or "BIG_BIN");

    public static string InspectionGuidance(Asset asset) => Code(asset) switch
    {
        "PROPERTY_UNIT" => "Check keys, access, walls, floors, windows, plumbing, electricity, furnishings and existing damage.",
        "RENTAL_VEHICLE" => "Check body, glass, tyres, lights, cabin, safety equipment, fuel and odometer.",
        "HEAVY_MACHINE" => "Check engine, hydraulics, tracks or tyres, cab, alarms, attachments, leaks and safety equipment.",
        "FORKLIFT" => "Check forks, mast, chains, hydraulics, tyres, brakes, horn, alarms and rated-capacity plate.",
        "GENSET" => "Check engine, battery, fuel, cooling, control panel, breakers, cables, earth stake and leaks.",
        "SCAFFOLD" => "Count frames, braces, platforms, pins, base plates and guardrails; check corrosion, distortion and damage.",
        "PORTABLE_TOILET" => "Check structure, tank, door, locks, vents, cleanliness, consumables and safe placement.",
        "BIG_BIN" => "Check body, floor, doors, lifting points, load limits, prohibited waste and safe placement.",
        _ => "Check condition, controls, guards, cables or hoses, accessories, safety labels and operation."
    };
}
