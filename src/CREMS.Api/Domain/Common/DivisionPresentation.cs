using System.Text.Json;
using System.Text.RegularExpressions;

namespace CREMS.Api.Domain.Common;

public sealed record PublicDivisionPresentation(string? DisplayName, string? LogoUrl, string? AccentColor);

public static class DivisionPresentation
{
    public static PublicDivisionPresentation Read(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "{}");
            string? Text(string key) => doc.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
            var name = Text("displayName"); var logo = Text("logoUrl"); var colour = Text("accentColor");
            return new(name is { Length: > 0 and <= 150 } ? name : null,
                Https(logo) ? logo : null, Colour(colour) ? colour : null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return new(null, null, null); }
    }
    public static bool IsValid(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "{}");
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var key in new[] { "displayName", "logoUrl", "accentColor" })
            {
                if (!doc.RootElement.TryGetProperty(key, out var value)) continue;
                if (value.ValueKind != JsonValueKind.String) return false;
                var text = value.GetString()?.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                if (key == "displayName" && text.Length > 150 || key == "logoUrl" && (text.Length > 2000 || !Https(text)) || key == "accentColor" && !Colour(text)) return false;
            }
            return true;
        }
        catch (JsonException) { return false; }
    }
    private static bool Https(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);
    private static bool Colour(string? value) => value is not null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$");
}
