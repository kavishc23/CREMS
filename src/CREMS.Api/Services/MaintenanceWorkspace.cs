using System.Text.Json;
using CREMS.Api.Domain.Assets;

namespace CREMS.Api.Services;

public static class MaintenanceWorkspace
{
    public static readonly string[] FinancialFields = ["useDetailedCosts", "hasEstimate", "estimatedCost", "actualCost", "partsCost", "labourCost", "transportCost", "externalServiceCost", "taxCost", "otherCost", "fuelCost", "labourHours", "labourRate", "invoiceNumber"];

    public static Dictionary<string, object?> Record(MaintenanceJob job, bool financial)
    {
        var record = job.GetType().GetProperties()
            .Where(p => p.Name is not ("Asset" or "Branch"))
            .ToDictionary(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name), p => p.GetValue(job));
        record["assetNumber"] = job.Asset?.AssetNumber;
        record["assetName"] = job.Asset?.Name;
        record["branchName"] = job.Branch?.Name;
        record["completionNotes"] = job.Description;
        record["version"] = job.UpdatedAt ?? job.CreatedAt;
        record["meterUnit"] = job.Asset?.MeterUnit;
        record["currentMeterReading"] = job.Asset?.CurrentMeterReading;
        record["assetStatus"] = job.Asset?.Status.ToString();
        if (!financial) foreach (var name in FinancialFields) record.Remove(name);
        return record;
    }

    private static IEnumerable<string> ReadChecks(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Array) {
            foreach (var child in node.EnumerateArray()) foreach (var label in ReadChecks(child)) yield return label;
        } else if (node.ValueKind == JsonValueKind.String) {
            if (!string.IsNullOrWhiteSpace(node.GetString())) yield return node.GetString()!;
        } else if (node.ValueKind == JsonValueKind.Object) {
            foreach (var key in new[] { "items", "sections", "checks" })
                if (node.TryGetProperty(key, out var children)) { foreach (var label in ReadChecks(children)) yield return label; yield break; }
            foreach (var key in new[] { "label", "name", "text" })
                if (node.TryGetProperty(key, out var text) && text.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(text.GetString())) { yield return text.GetString()!; yield break; }
        }
    }

    public static string[] Checks(Asset asset, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                using var doc = JsonDocument.Parse(configured);
                var items = ReadChecks(doc.RootElement).Distinct().ToArray();
                if (items.Length > 0) return items;
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException) { }
        }
        return asset.Type switch
        {
            AssetType.Vehicle or AssetType.PassengerVehicle or AssetType.CommercialVehicle => ["Repair verified and test run passed", "Brakes, tyres, lights and steering safe", "No fluid leaks or unresolved damage", "Tools, documents and accessories checked"],
            AssetType.PowerEquipment => ["Repair verified and test run passed", "Electrical connections and safety devices checked", "Oil, coolant and fuel systems checked", "No leaks or unresolved faults"],
            AssetType.PortableSanitation => ["Repair and cleaning completed", "Door, lock, shell and tank checked", "Consumables replenished and unit safe for hire"],
            AssetType.Scaffolding => ["Component quantities reconciled", "Frames, braces and planks checked", "Safety tags and damaged components resolved"],
            AssetType.WasteContainer => ["Structure, floor, doors and locks checked", "Cleaning and contamination checks passed", "No unresolved damage"],
            _ => ["Repair verified and test run passed", "Brakes, guards and safety devices checked", "Hydraulics, attachments and fluid systems checked", "No unresolved fault or damage"]
        };
    }
}
