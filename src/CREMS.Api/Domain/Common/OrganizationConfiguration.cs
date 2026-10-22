using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Rentals;

namespace CREMS.Api.Domain.Common;

public sealed record CustomerQuestion(
    string Code, string Label, string Type = "Text", bool Required = false,
    string? Help = null, IReadOnlyList<string>? Options = null,
    string? ServiceCode = null, string? CategoryCode = null);

public sealed record InspectionTemplateSnapshot(Guid Id, string Name, InspectionStage Stage, string ChecklistJson);
public sealed record BookingConfigurationSnapshot(
    IReadOnlyList<CustomerQuestion> Questions, IReadOnlyDictionary<string, string> Answers,
    IReadOnlyList<InspectionTemplateSnapshot> Templates, bool TracksMeter, bool TracksFuel,
    string? MeterUnit, DateTimeOffset CapturedAt);

public static class OrganizationConfiguration
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public static readonly string[] FieldTypes = ["Text", "LongText", "Number", "Integer", "Date", "Choice", "Boolean"];

    public static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}") return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); } catch (JsonException) { return null; }
    }

    public static string Merge<T>(string? original, T value)
    {
        JsonObject node;
        try { node = JsonNode.Parse(original ?? "{}") as JsonObject ?? new(); } catch (JsonException) { node = new(); }
        foreach (var property in JsonSerializer.SerializeToNode(value, Json)!.AsObject()) node[property.Key] = property.Value?.DeepClone();
        return node.ToJsonString(Json);
    }

    public static IReadOnlyList<CustomerQuestion> Questions(DivisionBookingFlow flow, ServiceOffering? service, AssetCategory? category) =>
        (flow.CustomerFields ?? []).Where(q =>
            (string.IsNullOrWhiteSpace(q.ServiceCode) || string.Equals(q.ServiceCode, service?.Code, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(q.CategoryCode) || string.Equals(q.CategoryCode, category?.Code, StringComparison.OrdinalIgnoreCase))).ToArray();

    public static string? ValidateQuestions(IReadOnlyList<CustomerQuestion>? questions)
    {
        if (questions is null) return null;
        if (questions.Count > 100) return "Use at most 100 customer questions per division.";
        if (questions.GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1)) return "Customer question codes must be unique in the division.";
        foreach (var q in questions)
        {
            if (string.IsNullOrWhiteSpace(q.Code) || !Regex.IsMatch(q.Code, "^[A-Za-z][A-Za-z0-9_]{0,39}$")) return "Question codes must start with a letter and use letters, numbers or underscores (maximum 40 characters).";
            if (string.IsNullOrWhiteSpace(q.Label) || q.Label.Length > 150 || (q.Help?.Length ?? 0) > 500 || !FieldTypes.Contains(q.Type)) return "Each question needs a label and a supported answer type.";
            if ((q.ServiceCode?.Length ?? 0) > 40 || (q.CategoryCode?.Length ?? 0) > 40) return "A question has an invalid service or category scope.";
            if (q.Type == "Choice" && (q.Options is not { Count: > 0 and <= 40 } || q.Options.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 150) || q.Options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != q.Options.Count)) return $"Provide unique, non-empty choices for {q.Label}.";
        }
        return null;
    }

    public static string? ValidateAnswers(IReadOnlyList<CustomerQuestion> fields, IReadOnlyDictionary<string, string>? answers)
    {
        if ((answers?.Count ?? 0) > 100 || answers?.Keys.Any(key => !fields.Any(f => f.Code == key)) == true) return "The request form changed. Refresh the asset and complete the current questions.";
        foreach (var f in fields)
        {
            var value = answers?.GetValueOrDefault(f.Code)?.Trim();
            if (string.IsNullOrEmpty(value)) { if (f.Required) return $"{f.Label} is required."; continue; }
            if (value.Length > (f.Type == "LongText" ? 2000 : 250)) return $"{f.Label} is too long.";
            if (f.Type == "Number" && !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _)) return $"Enter a number for {f.Label}.";
            if (f.Type == "Integer" && !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return $"Enter a whole number for {f.Label}.";
            if (f.Type == "Date" && !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return $"Enter a date for {f.Label}.";
            if (f.Type == "Choice" && f.Options?.Contains(value) != true) return $"Choose a valid option for {f.Label}.";
            if (f.Type == "Boolean" && value is not ("true" or "false")) return $"Choose yes or no for {f.Label}.";
        }
        return null;
    }

    public static IReadOnlyList<string> Checklist(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().SelectMany(section => section.GetProperty("items").EnumerateArray()
            .Select(item => $"{section.GetProperty("section").GetString()}: {item.GetProperty("label").GetString()}")).ToArray();
    }

    public static string? ValidateChecklist(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() is < 1 or > 20) return "Add at least one inspection section.";
            foreach (var section in doc.RootElement.EnumerateArray())
            {
                if (!section.TryGetProperty("section", out var title) || title.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(title.GetString()) || title.GetString()!.Length > 100) return "Every inspection section needs a title.";
                if (!section.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() is < 1 or > 80) return "Each inspection section needs 1–80 checks.";
                foreach (var item in items.EnumerateArray())
                    if (!item.TryGetProperty("label", out var label) || label.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(label.GetString()) || label.GetString()!.Length > 250) return "Every inspection check needs a label.";
            }
            var labels = Checklist(json);
            return labels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != labels.Count ? "Inspection check labels must be unique within a stage." : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { return "The inspection checklist is invalid."; }
    }

    public static bool? Flag(string? json, string name)
    {
        try { using var doc = JsonDocument.Parse(json ?? "{}"); return doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null; }
        catch (JsonException) { return null; }
    }
}
