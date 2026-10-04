using System.Text.Json.Serialization;
using CREMS.Api.Data;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.DataProtection;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// The Windows Event Log provider can throw when a non-elevated development
// process cannot access the .NET Runtime event source. Logging must never turn
// an otherwise recoverable API error into a failed request or host shutdown.
if (builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".data-protection")))
        .SetApplicationName("CREMS.Development");
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;
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
    options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
    options.SlidingExpiration = true;
    options.Events.OnValidatePrincipal = async context =>
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal?.Identity?.IsAuthenticated == true &&
            context.Properties.IssuedUtc is { } issued &&
            DateTimeOffset.UtcNow - issued > TimeSpan.FromMinutes(15))
        {
            context.RejectPrincipal();
        }
    };
});
builder.Services.AddAuthentication().AddCookie(SystemAuthenticationSchemes.Customer, options =>
{
    options.Cookie.Name = "CREMS.CustomerSession";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});

builder.Services.AddApplicationAuthorization();

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi(ApiDocumentation.Configure);
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
builder.Services.AddHttpClient("LicenceOcr", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddScoped<ILicenceOcrProvider, HttpLicenceOcrProvider>();
builder.Services.AddScoped<CustomerLicenceService>();
builder.Services.AddHttpClient("Brevo",client=>{client.BaseAddress=new Uri("https://api.brevo.com/");client.Timeout=TimeSpan.FromSeconds(20);});
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.Section));
builder.Services.AddHostedService<EmailDeliveryWorker>();
builder.Services.AddHostedService<BusinessAutomationWorker>();
builder.Services.AddHostedService<ActionNotificationWorker>();
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
    app.Use(async (context, next) =>
    {
        if (context.Request.Path == "/swagger/crems-environment.json")
        {
            var target = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { environment = app.Environment.EnvironmentName,
                server = target.DataSource, database = target.InitialCatalog });
            return;
        }
        var asset = context.Request.Path.Value switch
        {
            "/swagger/panel.js" => "panel.js",
            "/swagger/panel.css" => "panel.css",
            _ => null
        };
        if (asset is not null)
        {
            context.Response.ContentType = asset.EndsWith(".js") ? "application/javascript" : "text/css";
            context.Response.Headers.CacheControl = "no-store";
            await using var stream = typeof(Program).Assembly.GetManifestResourceStream($"CREMS.Api.Swagger.{asset}")!;
            await stream.CopyToAsync(context.Response.Body);
            return;
        }
        await next(context);
    });
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "CREMS API v1");
        options.DocumentTitle = "CREMS API — Swagger";
        options.InjectJavascript("/swagger/panel.js");
        options.InjectStylesheet("/swagger/panel.css");
        options.EnableFilter();
        options.EnableDeepLinking();
        options.DisplayRequestDuration();
        options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
        options.DefaultModelsExpandDepth(-1);
        options.ConfigObject.AdditionalItems["tagsSorter"] = "alpha";
        options.ConfigObject.AdditionalItems["operationsSorter"] = "method";
        options.UseRequestInterceptor("""
            (request) => {
                if (new URL(request.url, window.location.origin).pathname.startsWith('/openapi/')) {
                    request.credentials = 'omit';
                    return request;
                }
                const key = 'crems.swagger.windowId';
                let windowId = sessionStorage.getItem(key);
                if (!windowId) {
                    windowId = crypto.randomUUID();
                    sessionStorage.setItem(key, windowId);
                }
                request.headers['X-CREMS-Window-Id'] = windowId;
                request.credentials = 'same-origin';
                if (!window.cremsSwaggerGuard) throw new Error('Session panel is not ready. Refresh Swagger.');
                return window.cremsSwaggerGuard(request);
            }
            """.ReplaceLineEndings(" "));
    });
}

// The development launch profile is intentionally HTTP-only because Vite proxies
// same-origin /api requests to port 5080. Production still enforces HTTPS.
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
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
app.UseAuthorization();
app.UseMiddleware<WindowSessionMiddleware>();

app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

app.Run();

public partial class Program;
