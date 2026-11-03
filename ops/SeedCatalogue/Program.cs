using System.Security.Cryptography;
using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Controllers;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

if (!args.Contains("--apply")) throw new InvalidOperationException("Use --apply to add the local fictional dataset.");
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var apiRoot = Path.Combine(root, "src/CREMS.Api");
var config = new ConfigurationBuilder().SetBasePath(apiRoot).AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", optional: true).AddUserSecrets("crems-cs400-development").AddEnvironmentVariables().Build();
var connection = config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Missing database connection.");
var target = new SqlConnectionStringBuilder(connection);
if (target.InitialCatalog != "Crems" || !(target.DataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || target.DataSource.StartsWith("127.0.0.1", StringComparison.Ordinal)))
    throw new InvalidOperationException("This importer only targets the local Crems database.");
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);

await db.Database.MigrateAsync();
using var suppression=db.SuppressNotifications();

var divisions=await db.Divisions.Include(x=>x.ServiceOfferings).Where(x=>x.IsActive).ToListAsync();
var branch=await db.Branches.FirstAsync(x=>x.IsActive&&x.IsPublic);
foreach(var division in divisions.Where(x=>x.Name.Contains("Propert",StringComparison.OrdinalIgnoreCase))){
 var service=division.ServiceOfferings.FirstOrDefault(x=>x.IsActive);
 if(service is null)continue;
 if(!await db.BranchDivisions.AnyAsync(x=>x.DivisionId==division.Id&&x.BranchId==branch.Id))db.Add(new BranchDivision{DivisionId=division.Id,BranchId=branch.Id});
 if(!await db.BranchDivisionServices.AnyAsync(x=>x.DivisionId==division.Id&&x.BranchId==branch.Id&&x.ServiceOfferingId==service.Id))db.Add(new BranchDivisionService{DivisionId=division.Id,BranchId=branch.Id,ServiceOfferingId=service.Id});
 foreach(var label in new[]{"Office suite","Warehouse space","Retail space"}){
 var cat=await db.AssetCategories.FirstOrDefaultAsync(x=>x.DivisionId==division.Id&&x.Name==label);
 if(cat is null){cat=new AssetCategory{DivisionId=division.Id,ServiceOfferingId=service.Id,Name=label,Code=label.ToUpperInvariant().Replace(' ','_'),DefaultAssetType=AssetType.PropertyUnit};db.Add(cat);}
 if(!await db.Assets.AnyAsync(x=>x.AssetCategoryId==cat.Id))for(var j=1;j<=2;j++)db.Add(new Asset{AssetNumber=$"PRP-{cat.Code}-{j:000}",Name=label+ (j==1?" - Suva Central":" - Walu Bay"),DivisionId=division.Id,ServiceOfferingId=service.Id,AssetCategoryId=cat.Id,Category=label,BranchId=branch.Id,Type=AssetType.PropertyUnit,HireUnit=ChargeUnit.Month,DailyRate=label=="Warehouse space"?3500:label=="Office suite"?1800:2400,PhotoUrlsJson=JsonSerializer.Serialize(new[]{"/catalog/office-v2.png"})});
 }
}
await db.SaveChangesAsync();
var assets=await db.Assets.Include(x=>x.AssetCategory).Include(x=>x.AttributeValues).ThenInclude(x=>x.AttributeDefinition).ToListAsync();
var categories=await db.AssetCategories.ToListAsync();
var used=assets.Select(x=>x.AssetNumber).ToHashSet();
foreach(var c in categories){c.Code=c.Code.Replace("DEMO_", "");if(c.Name=="Bus")c.IsActive=false;}
foreach(var a in assets){
 var category=a.AssetCategory?.Name??a.Category??"Equipment";
 if(category=="Bus"||a.Name.Contains("Coach",StringComparison.OrdinalIgnoreCase)){a.IsActive=false;continue;}
 if(AssetCategoryPolicy.IsVehicle(a.Type)){if(!System.Text.RegularExpressions.Regex.IsMatch(a.AssetNumber,@"^LR[0-9]{4}$")){var reg=a.RegistrationNumber;if(string.IsNullOrWhiteSpace(reg)||used.Contains(reg)){do{reg=$"LR{Random.Shared.Next(10000):0000}";}while(!used.Add(reg));}a.AssetNumber=reg;}a.RegistrationNumber=null;}
 else if(a.AssetNumber.Contains("DEMO"))a.AssetNumber="AST-"+a.Id.ToString("N")[..8].ToUpperInvariant();
 if(a.SerialNumber?.Contains("DEMO")==true)a.SerialNumber="SN-"+a.Id.ToString("N")[..10].ToUpperInvariant();
 a.Name=System.Text.RegularExpressions.Regex.Replace(a.Name,@"\s*[·-]?\s*Demo\s*[0-9]*", "",System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
 a.Name=a.Name.Replace("�", "").Replace(" - Central Fleet", "").Replace(" - Western Fleet", "").Replace(" – Central Fleet", "").Replace(" – Western Fleet", "");
 foreach(var v in a.AttributeValues){if(v.Value=="Included"||v.Value=="Standard")v.Value=v.AttributeDefinition?.Code switch{"HDMI_INPUT"=>"Yes","BRIGHTNESS"=>"3600","POWER_SUPPLY"=>"Electric",_=>v.Value};}
}

foreach(var a in assets){
 var c=(a.AssetCategory?.Name??a.Category??"").ToLowerInvariant();
 foreach(var v in a.AttributeValues){var code=v.AttributeDefinition?.Code; if(code=="CAPACITY")v.Value=c switch{"big bin"=>"10 m³","forklift"=>"3 tonnes","water pump"=>"3 inch outlet","marquee"=>"72 m²","banquet furniture"=>"10 guests","storage container"=>"20 foot","pallet jack"=>"2.5 tonnes","plate compactor"=>"90 kg","pa speaker"=>"1000 W","concrete mixer" or "general hire equipment"=>"350 litres",_=>v.Value};}
 string? image=c switch{"hatchback"=>"hatchback","van"=>"cargo-van","office suite" or "office space"=>"office","warehouse space"=>"warehouse","retail space"=>"retail","pa speaker"=>"pa-speaker","marquee"=>"marquee","banquet furniture"=>"banquet","storage container"=>"container","pallet jack"=>"pallet-jack","plate compactor"=>"compactor","water pump"=>"water-pump","concrete mixer" or "general hire equipment"=>"concrete-mixer",_=>null};
 if(a.Name.Contains("Concrete Mixer",StringComparison.OrdinalIgnoreCase))image="concrete-mixer";
 if(image!=null)a.PhotoUrlsJson=JsonSerializer.Serialize(new[]{"/catalog/"+image+"-v2.png"});
 else if(c=="sedan")a.PhotoUrlsJson=JsonSerializer.Serialize(new[]{"/catalog/sedan.jpg"});
 else if(c=="pickup")a.PhotoUrlsJson=JsonSerializer.Serialize(new[]{"/catalog/pickup-v2.jpg"});
 else if(c=="truck")a.PhotoUrlsJson=JsonSerializer.Serialize(new[]{"/catalog/truck.jpg"});
 else if(c=="minivan")a.PhotoUrlsJson=JsonSerializer.Serialize(new[]{"/catalog/minibus.jpg"});
}
await db.SaveChangesAsync();
Console.WriteLine($"Active catalogue entries with demo wording: {assets.Count(a=>a.IsActive&&(a.Name.Contains("demo",StringComparison.OrdinalIgnoreCase)||a.AssetNumber.Contains("demo",StringComparison.OrdinalIgnoreCase)))}");
Console.WriteLine($"Active Bus categories: {categories.Count(c=>c.IsActive&&c.Name=="Bus")}");
Console.WriteLine($"Generated catalogue photos: {Directory.GetFiles(Path.Combine(root,"src/crems-web/public/catalog"),"*-v2.png").Length}");
Console.WriteLine($"Updated {assets.Count} catalogue records.");
