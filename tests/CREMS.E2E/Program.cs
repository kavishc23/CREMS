using System.Diagnostics;
using System.Text.Json.Serialization;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

// Disposable SQL + real Identity/controller host. No production seeding or email workers.
var root = Path.GetFullPath(args.Single());
var source = Environment.GetEnvironmentVariable("CREMS_TEST_SQL_CONNECTION")
    ?? throw new InvalidOperationException("Set CREMS_TEST_SQL_CONNECTION securely before running.");
var database = "CremsWorkspaceE2E_" + Guid.NewGuid().ToString("N");
var connection = new SqlConnectionStringBuilder(source) { InitialCatalog = database }.ConnectionString;
var password = "E2e!" + Guid.NewGuid().ToString("N");
var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
await using var db = new ApplicationDbContext(options);
WebApplication? app = null;
try
{
    await db.Database.MigrateAsync();
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", ContentRootPath = root });
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Error);
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection));
    builder.Services.AddIdentityApiEndpoints<ApplicationUser>().AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<ApplicationDbContext>();
    builder.Services.ConfigureApplicationCookie(o => {
        o.Cookie.Name = "CREMS.Session";
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
    });
    builder.Services.AddApplicationAuthorization();
    builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
    builder.Services.AddScoped<CurrentStaffScope>();
    builder.Services.AddSingleton<WindowSessionRegistry>();
    builder.Services.AddScoped<IEmailQueue, EmailQueue>();
    builder.Services.AddControllers().AddApplicationPart(typeof(BranchesController).Assembly)
        .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<MaintenanceConflictHandler>();
    builder.Services.AddRateLimiter(o => o.AddPolicy("login", context =>
        System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("e2e")));
    app = builder.Build();
    var files = new PhysicalFileProvider(Path.Combine(root, "src", "crems-web", "dist"));
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
    app.UseExceptionHandler();
    app.UseRouting();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseWhen(c => c.Request.Path.StartsWithSegments("/api"), branch => branch.UseMiddleware<WindowSessionMiddleware>());
    app.MapControllers();
    app.MapFallback(async context => { context.Response.ContentType = "text/html"; await context.Response.SendFileAsync(Path.Combine(root,"src","crems-web","dist","index.html")); });
    using (var scope = app.Services.CreateScope())
    {
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in SystemRoles.All) await roles.CreateAsync(new IdentityRole<Guid>(role));
        var division = new Division { Code = "SEED", Name = "Seed division" };
        var branch = new Branch { Code = "SEED", Name = "Seed branch" };
        db.AddRange(division, branch, new BranchDivision { BranchId = branch.Id, DivisionId = division.Id, IsActive = true });
        using (db.SuppressNotifications()) await db.SaveChangesAsync();
        var customer = new Customer { CustomerNumber = "E2ECUST", Name = "E2E Customer" };
        var booking = new Booking { BookingNumber = "E2EBOOK", BranchId = branch.Id, CustomerId = customer.Id, Status = BookingStatus.Confirmed };
        db.AddRange(customer, booking, new DispatchJob { DispatchNumber = "E2EDEL", BookingId = booking.Id, BranchId = branch.Id, Status = DispatchStatus.Arrived, ScheduledAt = DateTimeOffset.UtcNow });
        using (db.SuppressNotifications()) await db.SaveChangesAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var (name, role) in new[] { ("admin", SystemRoles.SuperAdministrator), ("manager", SystemRoles.BranchManager), ("officer", SystemRoles.RentalOfficer) })
        {
            var user = new ApplicationUser { UserName = name + "@example.test", Email = name + "@example.test", FullName = "E2E " + name, EmailConfirmed = true, BranchId = branch.Id, DivisionId = division.Id };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded) throw new InvalidOperationException("Test user creation failed.");
            await users.AddToRoleAsync(user, role);
        }
        db.RolePermissions.AddRange(new RolePermission { RoleName = SystemRoles.BranchManager, Permission = SystemPermissions.BranchesConfigure },
            new RolePermission { RoleName = SystemRoles.BranchManager, Permission = SystemPermissions.AssetCategoriesConfigure });
        using (db.SuppressNotifications()) await db.SaveChangesAsync();
    }
    await app.StartAsync();
    Console.WriteLine("Isolated SQL database migrated; real API and Identity host started.");
    var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false };
    start.ArgumentList.Add("tests/browser/organization-operations-e2e.cjs");
    start.Environment["CREMS_E2E_URL"] = app.Urls.Single();
    start.Environment["CREMS_E2E_PASSWORD"] = password;
    using var process = Process.Start(start)!;
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
    try { await process.WaitForExitAsync(timeout.Token); }
    catch { process.Kill(entireProcessTree: true); throw; }
    Environment.ExitCode = process.ExitCode;
}
finally
{
    if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
    if (new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog == database
        && database.StartsWith("CremsWorkspaceE2E_", StringComparison.Ordinal))
    {
        await db.Database.EnsureDeletedAsync();
        Console.WriteLine("Disposable E2E database deleted.");
    }
}
