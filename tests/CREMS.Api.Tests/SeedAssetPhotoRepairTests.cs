using System.Text.Json;
using CREMS.Api.Services;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class SeedAssetPhotoRepairTests
{
    [Fact]
    public void Restores_referenced_seed_without_overwriting_existing_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "crems-photo-test-" + Guid.NewGuid().ToString("N"));
        var apiRoot = Path.Combine(root, "api");
        var catalog = Path.Combine(root, "crems-web", "public", "catalog");
        Directory.CreateDirectory(catalog);
        try
        {
            File.WriteAllText(Path.Combine(catalog, "suv.jpg"), "bundled image");
            var id = Guid.NewGuid();
            var json = JsonSerializer.Serialize(new[] { $"/api/public/assets/{id}/photos/seed-suv.jpg" });
            Assert.Equal(1, SeedAssetPhotoRepair.Restore(apiRoot, id, json));
            var destination = Path.Combine(apiRoot, "App_Data", "asset-images", id.ToString("N"), "seed-suv.jpg");
            Assert.Equal("bundled image", File.ReadAllText(destination));
            File.WriteAllText(destination, "existing photo");
            Assert.Equal(0, SeedAssetPhotoRepair.Restore(apiRoot, id, json));
            Assert.Equal("existing photo", File.ReadAllText(destination));
            Assert.Equal(0, SeedAssetPhotoRepair.Restore(apiRoot, id, "invalid"));
            Assert.Equal(0, SeedAssetPhotoRepair.Restore(apiRoot, id,
                JsonSerializer.Serialize(new[] { $"/api/public/assets/{id}/photos/upload.jpg", $"/api/public/assets/{Guid.NewGuid()}/photos/seed-suv.jpg", $"/api/public/assets/{id}/photos/seed-../suv.jpg" })));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
