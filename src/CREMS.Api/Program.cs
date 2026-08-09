using System.Text.Json.Serialization;
using CREMS.Api.Data;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(SystemPolicies.StaffPortal, policy =>
        policy.RequireRole(
            SystemRoles.Administrator,
            SystemRoles.BranchManager,
            SystemRoles.RentalOfficer))
    .AddPolicy(SystemPolicies.AdministerSystem, policy =>
        policy.RequireRole(SystemRoles.Administrator))
    .AddPolicy(SystemPolicies.ManageBranch, policy =>
        policy.RequireRole(SystemRoles.Administrator, SystemRoles.BranchManager))
    .AddPolicy(SystemPolicies.ManageRentals, policy =>
        policy.RequireRole(
            SystemRoles.Administrator,
            SystemRoles.BranchManager,
            SystemRoles.RentalOfficer))
    .AddPolicy(SystemPolicies.ViewAssets, policy =>
        policy.RequireRole(
            SystemRoles.Administrator,
            SystemRoles.BranchManager,
            SystemRoles.RentalOfficer))
    .AddPolicy(SystemPolicies.ViewReports, policy =>
        policy.RequireRole(SystemRoles.Administrator, SystemRoles.BranchManager))
    .AddPolicy(SystemPolicies.CustomerPortal, policy =>
        policy.RequireRole(SystemRoles.Customer));

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddScoped<CurrentStaffScope>();
builder.Services.AddScoped<IEmailQueue, EmailQueue>();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.Section));
builder.Services.AddHostedService<EmailDeliveryWorker>();
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.Zero);
builder.Services.AddRateLimiter(options => options.AddPolicy("password-reset", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 })));

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
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGroup("/api/auth").MapIdentityApi<ApplicationUser>();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

public partial class Program;
