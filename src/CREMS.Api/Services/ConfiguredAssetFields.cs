using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class ConfiguredAssetFields
{
    public static async Task<string?> Apply(ApplicationDbContext db, Asset asset, IReadOnlyDictionary<string, string>? values, CancellationToken token)
    {
        // Optional input keeps older integrations compatible; the current editor always sends category values.
        if (!asset.AssetCategoryId.HasValue || values is null) return null;
        var definitions = await db.AssetAttributeDefinitions.Where(x => x.AssetCategoryId == asset.AssetCategoryId).ToListAsync(token);
        var fields = definitions.Select(x => new CustomerQuestion(x.Code, x.Name, x.DataType.ToString(), x.IsRequired,
            Options: JsonSerializer.Deserialize<string[]>(x.OptionsJson ?? "[]"))).ToArray();
        var error = OrganizationConfiguration.ValidateAnswers(fields, values);
        if (error is not null) return error;
        var existing = await db.AssetAttributeValues.Where(x => x.AssetId == asset.Id).ToListAsync(token);
        foreach (var definition in definitions)
        {
            var value = values.GetValueOrDefault(definition.Code)?.Trim();
            var row = existing.FirstOrDefault(x => x.AttributeDefinitionId == definition.Id);
            if (row is not null) row.Value = value;
            else if (!string.IsNullOrWhiteSpace(value)) db.AssetAttributeValues.Add(new AssetAttributeValue { AssetId = asset.Id, AttributeDefinitionId = definition.Id, Value = value });
        }
        return null;
    }
}
