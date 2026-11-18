using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
namespace CREMS.Api.Tests;
public sealed class ApplicationHttpSqlTests
{
    public static bool Enabled=>Environment.GetEnvironmentVariable("CREMS_RUN_SQL_TESTS")=="1";
    private sealed class Factory(string connection,string content):WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development").UseContentRoot(content);
            builder.ConfigureLogging(logging=>logging.ClearProviders());
            builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{
                ["ConnectionStrings:DefaultConnection"]=connection,["Email:Enabled"]="false",["BootstrapAdmin:Email"]="http-admin@example.test",["BootstrapAdmin:Password"]="TestingOnly123!",["BootstrapAdmin:FullName"]="HTTP training administrator",["DevelopmentData:RefreshOnStartup"]="false"}));
            builder.ConfigureTestServices(services=>{
                // Replace all database option callbacks before the startup initializer resolves a context.
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContextPool<ApplicationDbContext>(options=>options.UseSqlServer(connection));
                foreach(var descriptor in services.Where(x=>x.ServiceType==typeof(IHostedService)&&new[]{typeof(EmailDeliveryWorker),typeof(BusinessAutomationWorker),typeof(ActionNotificationWorker)}.Contains(x.ImplementationType)).ToArray())services.Remove(descriptor);
                services.PostConfigure<EmailOptions>(options=>options.Enabled=false);
            });
        }
    }
    [Fact(SkipUnless=nameof(Enabled),Skip="Enable disposable SQL HTTP integration tests.")]
    public async Task Real_HTTP_pipeline_enforces_sessions_serves_documentation_and_reads_seeded_workspaces()
    {
        var token=TestContext.Current.CancellationToken;
        var config=new ConfigurationBuilder().AddUserSecrets(typeof(ApplicationDbContext).Assembly,optional:true).Build();var source=Environment.GetEnvironmentVariable("CREMS_TEST_SQL_CONNECTION")??config.GetConnectionString("DefaultConnection");Assert.False(string.IsNullOrWhiteSpace(source));
        var name="CremsHttpTest_"+Guid.NewGuid().ToString("N");var connection=new SqlConnectionStringBuilder(source){InitialCatalog=name}.ConnectionString;var content=Path.Combine(Path.GetTempPath(),name);Directory.CreateDirectory(content);
        await using var cleanup=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
        var factory=new Factory(connection,content);
        try
        {
            using var client=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});
            client.DefaultRequestHeaders.Add("X-CREMS-Window-Id",Guid.NewGuid().ToString());
            using(var scope=factory.Services.CreateScope())Assert.Equal(name,new SqlConnectionStringBuilder(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString()).InitialCatalog);
            var health=await client.GetAsync("/api/health",token);Assert.Equal(HttpStatusCode.OK,health.StatusCode);Assert.Contains("healthy",await health.Content.ReadAsStringAsync(token));
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/assets",token)).StatusCode);
            var specification=await client.GetAsync("/openapi/v1.json",token);Assert.Equal(HttpStatusCode.OK,specification.StatusCode);using var schema=JsonDocument.Parse(await specification.Content.ReadAsStringAsync(token));Assert.Equal("CREMS API",schema.RootElement.GetProperty("info").GetProperty("title").GetString());var paths=schema.RootElement.GetProperty("paths");Assert.True(paths.EnumerateObject().Count()>50);Assert.True(paths.TryGetProperty("/api/reporting/workspace",out var reporting));Assert.Contains("Fiji",reporting.GetProperty("get").GetProperty("description").GetString());Assert.True(schema.RootElement.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("StaffSession",out _));
            foreach(var path in new[]{"/swagger/panel.js","/swagger/panel.css","/swagger/crems-environment.json"})Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(path,token)).StatusCode);
            var login=await client.PostAsJsonAsync("/api/auth/login",new{email="http-admin@example.test",password="TestingOnly123!"},token);Assert.Equal(HttpStatusCode.NoContent,login.StatusCode);
            var session=await client.GetAsync("/api/auth/session",token);Assert.Equal(HttpStatusCode.OK,session.StatusCode);Assert.Contains("HTTP training administrator",await session.Content.ReadAsStringAsync(token));
            foreach(var path in new[]{"/api/assets?status=Available&pageSize=10","/api/customers?pageSize=10","/api/bookings/work-queue?pageSize=10","/api/rentals/work-queue?pageSize=10","/api/reporting/workspace?from=2026-01-01&to=2026-12-31","/api/public/catalogue-bootstrap"}){var response=await client.GetAsync(path,token);Assert.True(response.IsSuccessStatusCode,$"{path} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(token)}");}
            Dictionary<string,string> scopeIds;
            using(var scope=factory.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                scopeIds=new(){["assetId"]=(await db.Assets.Select(x=>x.Id).FirstAsync(token)).ToString(),["branchId"]=(await db.Branches.Select(x=>x.Id).FirstAsync(token)).ToString(),["divisionId"]=(await db.Divisions.Select(x=>x.Id).FirstAsync(token)).ToString(),["bookingId"]=(await db.Bookings.Select(x=>x.Id).FirstAsync(token)).ToString(),["customerId"]=(await db.Customers.Select(x=>x.Id).FirstAsync(token)).ToString()};
            }
            // Exercise documented read endpoints through routing, filters, authorization and SQL translation.
            // Customer-only routes are covered with customer identities in their own suite.
            foreach(var path in paths.EnumerateObject().Where(path=>path.Name.StartsWith("/api/")&&!path.Name.Contains('{')&&!path.Name.StartsWith("/api/customer-account/")&&path.Value.TryGetProperty("get",out _)))
            {
                var operation=path.Value.GetProperty("get");
                if(operation.TryGetProperty("parameters",out var parameters)&&parameters.EnumerateArray().Any(parameter=>parameter.TryGetProperty("required",out var required)&&required.ValueKind==JsonValueKind.True))continue;
                var query=new List<string>();
                scopeIds["from"]="2026-01-01";scopeIds["to"]="2026-01-31";
                if(operation.TryGetProperty("parameters",out parameters))foreach(var parameter in parameters.EnumerateArray())
                {
                    var key=parameter.GetProperty("name").GetString()!;
                    if(scopeIds.TryGetValue(key,out var id))query.Add(Uri.EscapeDataString(key)+"="+id);
                }
                var response=await client.GetAsync(path.Name+(query.Count>0?"?"+string.Join('&',query):""),token);
                Assert.True(response.IsSuccessStatusCode,$"Documented read {path.Name} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(token)}");
                if(response.Content.Headers.ContentType?.MediaType=="application/json")using(JsonDocument.Parse(await response.Content.ReadAsStringAsync(token))) { }
            }
            using(var identity=JsonDocument.Parse(await session.Content.ReadAsStringAsync(token)))
            {
                var userId=identity.RootElement.GetProperty("id").GetGuid();
                var message=await client.PostAsJsonAsync("/api/notifications/staff",new{requestId=Guid.NewGuid(),recipientUserIds=new[]{userId},title="HTTP training message",message="Only in the disposable test database",severity="Info"},token);Assert.Equal(HttpStatusCode.OK,message.StatusCode);
                using var inbox=JsonDocument.Parse(await client.GetStringAsync("/api/notifications",token));
                var notice=inbox.RootElement.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("title").GetString()=="HTTP training message");var noticeId=notice.GetProperty("id").GetGuid();Assert.False(notice.GetProperty("isRead").GetBoolean());
                for(var attempt=0;attempt<2;attempt++)Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync("/api/notifications/read",new{ids=new[]{noticeId}},token)).StatusCode);
                using var readInbox=JsonDocument.Parse(await client.GetStringAsync("/api/notifications",token));Assert.True(readInbox.RootElement.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==noticeId).GetProperty("isRead").GetBoolean());
            }
            var invalid=await client.PatchAsJsonAsync($"/api/bookings/{Guid.NewGuid()}/status",new{status="InvalidStatus",note="Training validation"},token);Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsync("/api/auth/logout",null,token)).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/assets",token)).StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
            var actual=new SqlConnectionStringBuilder(cleanup.Database.GetConnectionString()).InitialCatalog;if(actual!=name||!actual.StartsWith("CremsHttpTest_"))throw new InvalidOperationException("Refusing cleanup outside the disposable HTTP database.");
            await cleanup.Database.EnsureDeletedAsync(CancellationToken.None);if(Directory.Exists(content))Directory.Delete(content,true);
        }
    }
}
