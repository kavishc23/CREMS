using System.Text.Json.Serialization;
using CREMS.Api.Data;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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
        policy.RequireRole(SystemRoles.Administrator, SystemRoles.BranchManager));

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddScoped<CurrentStaffScope>();

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
app.UseAuthentication();
app.UseAuthorization();

app.MapGroup("/api/auth").MapIdentityApi<ApplicationUser>();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

public partial class Program;
