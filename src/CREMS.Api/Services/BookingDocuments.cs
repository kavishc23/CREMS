using System.Text.Json;
using CREMS.Api.Domain.Assets;

namespace CREMS.Api.Services;

public static class BookingDocuments
{
    public static string[] Required(Asset? asset)
    {
        try { return (JsonSerializer.Deserialize<string[]>(asset?.ServiceOffering?.RequiredDocumentsJson ?? "[]") ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch (JsonException) { return []; }
    }

    public static bool ValidFile(string extension, byte[] bytes) => extension switch
    {
        ".pdf" => bytes.AsSpan().StartsWith("%PDF"u8),
        ".jpg" or ".jpeg" => bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 }),
        ".png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        _ => false
    };
}
