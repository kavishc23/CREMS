using System.Text.Json;

namespace CREMS.Api.Services;

public static class SeedAssetPhotoRepair
{
    // Restore only referenced bundled photos; never replace staff uploads or metadata.
    public static int Restore(string contentRoot, Guid assetId, string photoUrlsJson)
    {
        string[] urls;
        try { urls = JsonSerializer.Deserialize<string[]>(photoUrlsJson) ?? []; }
        catch (JsonException) { return 0; }
        var restored = 0;
        var prefix = $"/api/public/assets/{assetId}/photos/";
        foreach (var url in urls)
        {
            if (url is null || !url.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var fileName = url[prefix.Length..];
            if (!fileName.StartsWith("seed-", StringComparison.Ordinal) ||
                fileName.Contains('/') || fileName.Contains((char)92) || fileName.Contains(':') ||
                Path.GetExtension(fileName) is not (".jpg" or ".jpeg" or ".png" or ".webp")) continue;
            var source = Path.Combine(contentRoot, "..", "crems-web", "public", "catalog", fileName[5..]);
            var directory = Path.Combine(contentRoot, "App_Data", "asset-images", assetId.ToString("N"));
            var destination = Path.Combine(directory, fileName);
            if (!File.Exists(source) || File.Exists(destination)) continue;
            Directory.CreateDirectory(directory);
            File.Copy(source, destination, overwrite: false);
            restored++;
        }
        return restored;
    }
}
