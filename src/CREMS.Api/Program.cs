using System.Text.Json.Serialization;
using CREMS.Api.Data;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.ResponseCompression;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.User.RequireUniqueEmail = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "CREMS.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    // Fixed session lifetime. Activity does not extend this period.
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = false;
    options.Events.OnValidatePrincipal = async context =>
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal?.Identity?.IsAuthenticated == true &&
            context.Properties.IssuedUtc is { } issued &&
            DateTimeOffset.UtcNow - issued > TimeSpan.FromMinutes(30))
        {
            context.RejectPrincipal();
        }
    };
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(SystemPolicies.StaffPortal, policy =>
        policy.RequireRole(SystemRoles.Staff))
    .AddPolicy(SystemPolicies.AdministerSystem, policy =>
        policy.RequireRole(SystemRoles.SuperAdministrator))
    .AddPolicy(SystemPolicies.ManageUsers, policy =>
        policy.RequireRole(SystemRoles.SuperAdministrator, SystemRoles.Administrator))
    .AddPolicy(SystemPolicies.ManageDivisions, policy =>
        policy.RequireRole(SystemRoles.SuperAdministrator))
    .AddPolicy(SystemPolicies.ManageBranch, policy =>
        policy.RequireRole(SystemRoles.SuperAdministrator, SystemRoles.Administrator, SystemRoles.BranchManager))
    .AddPolicy(SystemPolicies.ManageRentals, policy =>
        policy.RequireRole(
            SystemRoles.SuperAdministrator,
            SystemRoles.Administrator,
            SystemRoles.BranchManager,
            SystemRoles.RentalOfficer))
    .AddPolicy(SystemPolicies.ViewAssets, policy =>
        policy.RequireRole(
            SystemRoles.SuperAdministrator,
            SystemRoles.Administrator,
            SystemRoles.BranchManager,
            SystemRoles.RentalOfficer,
            SystemRoles.MaintenanceOfficer))
    .AddPolicy(SystemPolicies.ManageMaintenance, policy =>
        policy.RequireRole(SystemRoles.SuperAdministrator, SystemRoles.Administrator, SystemRoles.BranchManager, SystemRoles.MaintenanceOfficer))
    .AddPolicy(SystemPolicies.ManageFinance, policy =>
        policy.RequireRole(SystemRoles.SuperAdministrator, SystemRoles.Administrator, SystemRoles.BranchManager, SystemRoles.FinanceOfficer))
    .AddPolicy(SystemPolicies.UseAssetQr, policy =>
        policy.RequireRole(SystemRoles.SuperAdministrator, SystemRoles.Administrator, SystemRoles.BranchManager, SystemRoles.RentalOfficer, SystemRoles.MaintenanceOfficer))
    .AddPolicy(SystemPolicies.ViewReports, policy =>
        policy.RequireRole(SystemRoles.SuperAdministrator, SystemRoles.Administrator, SystemRoles.BranchManager, SystemRoles.FinanceOfficer))
    .AddPolicy(SystemPolicies.CustomerPortal, policy =>
        policy.RequireRole(SystemRoles.Customer))
    .AddPolicy(SystemPermissions.UsersCreate, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.UsersCreate)))
    .AddPolicy(SystemPermissions.UsersResetPassword, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.UsersResetPassword)))
    .AddPolicy(SystemPermissions.UsersManageAccess, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.UsersManageAccess)))
    .AddPolicy(SystemPermissions.CustomersManageAccess, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.CustomersManageAccess)))
    .AddPolicy(SystemPermissions.RentalsApprove, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.RentalsApprove)))
    .AddPolicy(SystemPermissions.MaintenanceComplete, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.MaintenanceComplete)))
    .AddPolicy(SystemPermissions.PaymentsRefund, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.PaymentsRefund)))
    .AddPolicy(SystemPermissions.ReportsFinancial, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.ReportsFinancial)))
    .AddPolicy(SystemPermissions.DivisionsConfigure, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.DivisionsConfigure)))
    .AddPolicy(SystemPermissions.BranchesConfigure, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.BranchesConfigure)))
    .AddPolicy(SystemPermissions.ServicesConfigure, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.ServicesConfigure)))
    .AddPolicy(SystemPermissions.AssetCategoriesConfigure, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetCategoriesConfigure)))
    .AddPolicy(SystemPermissions.BranchCalendarManage, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.BranchCalendarManage)))
    .AddPolicy(SystemPermissions.PricingConfigure, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.PricingConfigure)))
    .AddPolicy(SystemPermissions.AssetsView, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetsView)))
    .AddPolicy(SystemPermissions.AssetsCreate, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetsCreate)))
    .AddPolicy(SystemPermissions.AssetsEdit, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetsEdit)))
    .AddPolicy(SystemPermissions.AssetsTransfer, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetsTransfer)))
    .AddPolicy(SystemPermissions.AssetsInspect, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetsInspect)))
    .AddPolicy(SystemPermissions.AssetsRecordMeter, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetsRecordMeter)))
    .AddPolicy(SystemPermissions.AssetsRetire, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetsRetire)))
    .AddPolicy(SystemPermissions.AssetsViewFinancials, policy => policy.AddRequirements(new PermissionRequirement(SystemPermissions.AssetsViewFinancials)));

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    options.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.AddRequestTimeouts(options =>
    options.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(30),
        TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
    });
builder.Services.AddSingleton<WindowSessionRegistry>();
builder.Services.AddScoped<CurrentStaffScope>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddScoped<IEmailQueue, EmailQueue>();
builder.Services.AddScoped<RentalPricingService>();
builder.Services.AddHttpClient("Brevo",client=>{client.BaseAddress=new Uri("https://api.brevo.com/");client.Timeout=TimeSpan.FromSeconds(20);});
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.Section));
builder.Services.AddHostedService<EmailDeliveryWorker>();
builder.Services.AddHostedService<BusinessAutomationWorker>();
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    // Session revocation is still enforced by the window-session middleware.
    // Avoid a user-store lookup on every authenticated API request.
    options.ValidationInterval = TimeSpan.FromMinutes(5));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 180, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("password-reset", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
});

builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
    policy.WithOrigins(builder.Configuration["FrontendUrl"] ?? "http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

var app = builder.Build();

await app.InitializeAsync();

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    context.Response.Headers["Cache-Control"] = "no-store";
    await next();
});
app.UseCors("Frontend");
app.UseRequestTimeouts();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<WindowSessionMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

public partial class Program;
