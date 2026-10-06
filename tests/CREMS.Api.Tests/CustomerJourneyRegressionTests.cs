using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace CREMS.Api.Tests;

// Prepared with Codex assistance. Real Identity services, isolated EF in-memory data;
// controller calls do not exercise HTTP routing, authorization filters or SQL transactions.
public sealed class CustomerJourneyRegressionTests
{
    [Fact]
    public async Task Registration_creates_customer_role_verification_queue_and_customer_cookie()
    {
        await using var f = await Fixture.Create();
        var result = await f.Account.Register(new("  Portfolio Customer  ", " NEW@EXAMPLE.TEST ", " 1234567 ", " Suva ", Fixture.Password), TestContext.Current.CancellationToken);
        Assert.IsType<CreatedAtActionResult>(result);
        var customer = await f.Db.Customers.SingleAsync(x => x.Email == "new@example.test", TestContext.Current.CancellationToken);
        Assert.Equal("Portfolio Customer", customer.Name);
        Assert.Equal("1234567", customer.Phone);
        Assert.Equal("Suva", customer.Address);
        Assert.Empty(customer.HirePreferences);
        var user = await f.Users.FindByEmailAsync("new@example.test");
        Assert.NotNull(user);
        Assert.Equal(customer.Id, user.CustomerId);
        Assert.True(await f.Users.IsInRoleAsync(user, SystemRoles.Customer));
        Assert.False(user.EmailConfirmed);
        Assert.Single(await f.Db.OutboundEmails.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Contains(f.Context.Response.Headers.SetCookie, cookie => cookie!.StartsWith("CREMS.CustomerSession="));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Registration_rejects_existing_identity_or_customer_email_without_new_records(bool identity)
    {
        await using var f = await Fixture.Create();
        var email = identity ? f.User.Email! : "record@example.test";
        if (!identity) { f.Db.Customers.Add(new() { CustomerNumber = "CUS-000099", Name = "Existing", Email = email }); await f.Db.SaveChangesAsync(TestContext.Current.CancellationToken); }
        var before = await f.Db.Customers.CountAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConflictObjectResult>(await f.Account.Register(new("New", email, "1234567", null, Fixture.Password), TestContext.Current.CancellationToken));
        Assert.Equal(before, await f.Db.Customers.CountAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await f.Db.OutboundEmails.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Registration_rejects_weak_password_without_creating_customer_or_cookie()
    {
        await using var f = await Fixture.Create();
        var result = await f.Account.Register(new("New", "new@example.test", "1234567", null, "weak"), TestContext.Current.CancellationToken);
        Assert.Equal(400, Assert.IsType<BadRequestObjectResult>(result).StatusCode);
        Assert.Null(await f.Users.FindByEmailAsync("new@example.test"));
        Assert.False(await f.Db.Customers.AnyAsync(x => x.Email == "new@example.test", TestContext.Current.CancellationToken));
        Assert.Equal(0, f.Context.Response.Headers.SetCookie.Count);
    }

    [Theory]
    [InlineData("", "valid@example.test", "1234567", "Portfolio123!")]
    [InlineData("New", "invalid-email", "1234567", "Portfolio123!")]
    [InlineData("New", "valid@example.test", "", "Portfolio123!")]
    [InlineData("New", "valid@example.test", "1234567", "short")]
    public void Registration_request_annotations_reject_invalid_fields(string name, string email, string phone, string password)
    {
        var request = new RegisterCustomerRequest(name, email, phone, null, password);
        // MVC uses constructor-parameter validation metadata for positional records.
        var parameters = typeof(RegisterCustomerRequest).GetConstructors().Single().GetParameters();
        var values = new object?[] { request.FullName, request.Email, request.Phone, request.Address, request.Password };
        Assert.Contains(parameters.SelectMany((parameter, index) => parameter.GetCustomAttributes(typeof(ValidationAttribute), false)
            .Cast<ValidationAttribute>().Select(attribute => attribute.IsValid(values[index]))), valid => !valid);
    }

    [Fact]
    public async Task Customer_login_sets_customer_cookie_and_records_success()
    {
        await using var f = await Fixture.Create();
        Assert.IsType<NoContentResult>(await f.Login.Login(new(f.User.Email!, Fixture.Password)));
        Assert.Contains(f.Context.Response.Headers.SetCookie, cookie => cookie!.StartsWith("CREMS.CustomerSession="));
        Assert.DoesNotContain(f.Context.Response.Headers.SetCookie, cookie => cookie!.StartsWith("CREMS.StaffSession="));
        Assert.Single(await f.Db.UserSessions.ToListAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(f.User.LastLoginAt);
    }

    [Theory]
    [InlineData("wrong-password", false)]
    [InlineData("Portfolio123!", true)]
    public async Task Customer_login_rejects_wrong_password_or_disabled_identity(string password, bool disabled)
    {
        await using var f = await Fixture.Create();
        f.User.IsActive = !disabled;
        await f.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.IsType<UnauthorizedResult>(await f.Login.Login(new(f.User.Email!, password)));
        Assert.Equal(0, f.Context.Response.Headers.SetCookie.Count);
        Assert.Empty(await f.Db.UserSessions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Account_session_forbids_blocked_or_inactive_customer(bool blocked, bool inactive)
    {
        await using var f = await Fixture.Create();
        f.Customer.IsBlocked = blocked; f.Customer.IsActive = !inactive;
        await f.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ForbidResult>(await f.Account.Session());
    }

    [Fact]
    public async Task Preferences_persist_multiple_distinct_values_and_can_be_cleared()
    {
        await using var f = await Fixture.Create();
        Assert.IsType<OkObjectResult>(await f.Account.UpdateProfile(new(" 7654321 ", " Suva ", [CustomerHirePreference.Vehicles, CustomerHirePreference.Equipment, CustomerHirePreference.Vehicles]), TestContext.Current.CancellationToken));
        f.Db.ChangeTracker.Clear();
        var saved = await f.Db.Customers.SingleAsync(x => x.Id == f.Customer.Id, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { CustomerHirePreference.Vehicles, CustomerHirePreference.Equipment }, saved.HirePreferences);
        Assert.Equal("7654321", saved.Phone); Assert.Equal("Suva", saved.Address);
        Assert.IsType<OkObjectResult>(await f.Account.UpdateProfile(new("7654321", "  ", []), TestContext.Current.CancellationToken));
        f.Db.ChangeTracker.Clear();
        saved = await f.Db.Customers.SingleAsync(x => x.Id == f.Customer.Id, TestContext.Current.CancellationToken);
        Assert.Empty(saved.HirePreferences); Assert.Null(saved.Address);
    }

    [Fact]
    public async Task Dashboard_booking_list_and_details_do_not_expose_another_customer()
    {
        await using var f = await Fixture.Create();
        var own = f.Booking(f.Customer.Id); var other = f.Booking(Guid.NewGuid());
        f.Db.AddRange(own, other); await f.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var list = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await f.Account.Bookings(TestContext.Current.CancellationToken)).Value);
        Assert.Single(list.EnumerateArray());
        Assert.Equal(own.BookingNumber, list[0].GetProperty("Reference").GetString());
        Assert.IsType<OkObjectResult>(await f.Account.BookingDetails(own.Id, TestContext.Current.CancellationToken));
        Assert.IsType<NotFoundResult>(await f.Account.BookingDetails(other.Id, TestContext.Current.CancellationToken));
        Assert.IsType<NotFoundResult>(await f.Account.BookingDetails(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Anonymous_customer_data_access_is_rejected()
    {
        await using var f = await Fixture.Create();
        f.Context.User = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.IsType<UnauthorizedResult>(await f.Account.Bookings(TestContext.Current.CancellationToken));
        Assert.IsType<UnauthorizedResult>(await f.Account.UpdateProfile(new("1234567", null, []), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Booking_date_change_rejects_equal_or_reversed_dates_without_mutation(int days)
    {
        await using var f = await Fixture.Create();
        var booking = f.Booking(f.Customer.Id); f.Db.Add(booking); await f.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var original = booking.Items.Single().StartAt;
        var start = DateTimeOffset.UtcNow.AddDays(40);
        Assert.IsType<BadRequestObjectResult>(await f.Account.ChangeDates(booking.Id, new(start, start.AddDays(days)), TestContext.Current.CancellationToken));
        Assert.Equal(original, booking.Items.Single().StartAt);
    }

    [Fact]
    public async Task Draft_booking_dates_can_change_but_another_customer_cannot_change_them()
    {
        await using var f = await Fixture.Create();
        var own = f.Booking(f.Customer.Id); var other = f.Booking(Guid.NewGuid());
        f.Db.AddRange(own, other); await f.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var start = DateTimeOffset.UtcNow.AddDays(40); var end = start.AddDays(3);
        Assert.IsType<OkObjectResult>(await f.Account.ChangeDates(own.Id, new(start, end), TestContext.Current.CancellationToken));
        Assert.Equal(start, own.Items.Single().StartAt); Assert.Equal(end, own.Items.Single().EndAt);
        Assert.IsType<NotFoundResult>(await f.Account.ChangeDates(other.Id, new(start, end), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Catalogue_rejects_invalid_date_ranges(int days)
    {
        await using var f = await Fixture.Create();
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        var result = await f.Public.GetAssets(null, null, null, start, start.AddDays(days), TestContext.Current.CancellationToken);
        Assert.Equal(400, Assert.IsType<BadRequestObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task Catalogue_excludes_private_divisions_and_unavailable_operational_states()
    {
        await using var f = await Fixture.Create();
        var visible = await f.Public.GetAssets(null, null, null, null, null, TestContext.Current.CancellationToken);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<PublicAssetResponse>>(Assert.IsType<OkObjectResult>(visible.Result).Value));
        f.Asset.Status = AssetStatus.Maintenance; await f.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<PublicAssetResponse>>(Assert.IsType<OkObjectResult>((await f.Public.GetAssets(null, null, null, null, null, TestContext.Current.CancellationToken)).Result).Value));
        f.Asset.Status = AssetStatus.Available; f.Division.IsPublic = false; await f.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<PublicAssetResponse>>(Assert.IsType<OkObjectResult>((await f.Public.GetAssets(null, null, null, null, null, TestContext.Current.CancellationToken)).Result).Value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(367)]
    public async Task Public_booking_rejects_invalid_duration_without_persisting_request(int days)
    {
        await using var f = await Fixture.Create();
        var request = f.PublicRequest();
        var result = await f.Public.RequestBooking(request with { EndDate = request.StartDate.AddDays(days) }, TestContext.Current.CancellationToken);
        Assert.Equal(400, Assert.IsType<BadRequestObjectResult>(result.Result).StatusCode);
        Assert.Empty(await f.Db.Bookings.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await f.Db.OutboundEmails.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Public_booking_rejects_blocked_customer_or_missing_verified_vehicle_licence(bool blocked)
    {
        await using var f = await Fixture.Create();
        f.Customer.IsBlocked = blocked; await f.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var result = await f.Public.RequestBooking(f.PublicRequest(), TestContext.Current.CancellationToken);
        var rejected = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(400, rejected.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(rejected.Value);
        if (blocked) Assert.Contains("cannot accept", problem.Detail);
        else Assert.Contains("DriverLicence", problem.Errors.Keys);
        Assert.Empty(await f.Db.Bookings.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Public_booking_uses_registered_identity_and_verified_licence_instead_of_submitted_identity()
    {
        await using var f = await Fixture.Create();
        f.Db.CustomerLicences.Add(new() { CustomerId = f.Customer.Id, StorageKey = "test-only.png", ContentType = "image/png", ContentHash = "test-only", LicenceNumber = "TEST-LICENCE", Status = LicenceVerificationStatus.Verified });
        await f.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var response = await f.Public.RequestBooking(f.PublicRequest(), TestContext.Current.CancellationToken);
        var submitted = Assert.IsType<PublicBookingResponse>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal("Booking", submitted.RequestType); Assert.StartsWith("BKR-", submitted.Reference);
        var booking = await f.Db.Bookings.Include(b => b.Items).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(f.Customer.Id, booking.CustomerId);
        Assert.Single(booking.Items); Assert.Equal(100m, booking.Items.Single().DailyRate);
        Assert.Contains("TEST-LICENCE", booking.Notes);
        Assert.DoesNotContain("Spoofed", booking.Notes);
        Assert.Equal("customer@example.test", f.Customer.Email);
        Assert.Single(await f.Db.OutboundEmails.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Confirmed_booking_date_change_requires_branch_review_and_prevents_duplicate_requests()
    {
        await using var f = await Fixture.Create();
        var booking = f.Booking(f.Customer.Id); booking.Status = BookingStatus.Confirmed;
        f.Db.Add(booking); await f.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var original = booking.Items.Single().StartAt;
        var start = DateTimeOffset.UtcNow.AddDays(40); var request = new CustomerBookingDatesRequest(start, start.AddDays(3));
        Assert.IsType<AcceptedResult>(await f.Account.ChangeDates(booking.Id, request, TestContext.Current.CancellationToken));
        Assert.Equal(original, booking.Items.Single().StartAt);
        Assert.Single(await f.Db.CustomerCases.ToListAsync(TestContext.Current.CancellationToken));
        Assert.IsType<ConflictObjectResult>(await f.Account.ChangeDates(booking.Id, request, TestContext.Current.CancellationToken));
        Assert.Single(await f.Db.CustomerCases.ToListAsync(TestContext.Current.CancellationToken));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string Password = "Portfolio123!";
        private readonly ServiceProvider services;
        public ApplicationDbContext Db { get; }
        public UserManager<ApplicationUser> Users { get; }
        public ApplicationUser User { get; private set; } = null!;
        public Customer Customer { get; } = new() { CustomerNumber = "CUS-000001", Name = "Portfolio Customer", Email = "customer@example.test", Phone = "1234567" };
        public Branch Branch { get; } = new() { Code = "TEST", Name = "Test branch" };
        public Division Division { get; } = new() { Code = "MOTORS", Name = "Carpenters Motors" };
        public Asset Asset { get; private set; } = null!;
        public DefaultHttpContext Context { get; }
        public CustomerAccountController Account { get; }
        public SessionController Login { get; }
        public PublicRentalsController Public { get; }

        private Fixture(ServiceProvider services)
        {
            this.services = services;
            Db = services.GetRequiredService<ApplicationDbContext>(); Users = services.GetRequiredService<UserManager<ApplicationUser>>();
            Context = new() { RequestServices = services };
            Context.Request.Headers["X-CREMS-Window-Id"] = Guid.NewGuid().ToString();
            services.GetRequiredService<IHttpContextAccessor>().HttpContext = Context;
            var claims = services.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
            var environment = new TestEnvironment();
            Account = new(Db, Users, claims, new EmailQueue(), environment) { ControllerContext = new() { HttpContext = Context } };
            Login = new(Users, services.GetRequiredService<SignInManager<ApplicationUser>>(), claims, Db, new WindowSessionRegistry(), new EmailQueue()) { ControllerContext = new() { HttpContext = Context } };
            Public = new(Db, Users, new EmailQueue(), environment) { ControllerContext = new() { HttpContext = Context } };
        }

        public static async Task<Fixture> Create()
        {
            var collection = new ServiceCollection();
            collection.AddLogging(); collection.AddMvcCore(); collection.AddDataProtection().UseEphemeralDataProtectionProvider(); collection.AddHttpContextAccessor();
            collection.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase($"customer-journey-{Guid.NewGuid()}").ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            collection.AddIdentityCore<ApplicationUser>(options => { options.Password.RequiredLength = 10; options.SignIn.RequireConfirmedEmail = false; })
                .AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<ApplicationDbContext>().AddSignInManager().AddDefaultTokenProviders();
            collection.AddAuthentication(IdentityConstants.ApplicationScheme)
                .AddCookie(IdentityConstants.ApplicationScheme, options => options.Cookie.Name = "CREMS.StaffSession")
                .AddCookie(SystemAuthenticationSchemes.Customer, options => options.Cookie.Name = "CREMS.CustomerSession");
            var f = new Fixture(collection.BuildServiceProvider());
            await f.Db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            f.Asset = new() { AssetNumber = "TEST-001", Name = "Test vehicle", Type = AssetType.Vehicle, BranchId = f.Branch.Id, DivisionId = f.Division.Id, DailyRate = 100m };
            f.Db.AddRange(f.Customer, f.Branch, f.Division, f.Asset, new BranchDivision { BranchId = f.Branch.Id, DivisionId = f.Division.Id });
            using (f.Db.SuppressNotifications()) await f.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
            var role = await f.services.GetRequiredService<RoleManager<IdentityRole<Guid>>>().CreateAsync(new(SystemRoles.Customer));
            Assert.True(role.Succeeded);
            f.User = new() { Id = Guid.NewGuid(), Email = f.Customer.Email, UserName = f.Customer.Email, FullName = f.Customer.Name, CustomerId = f.Customer.Id };
            var created = await f.Users.CreateAsync(f.User, Password); Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));
            Assert.True((await f.Users.AddToRoleAsync(f.User, SystemRoles.Customer)).Succeeded);
            f.Context.User = await f.services.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(f.User);
            return f;
        }

        public Booking Booking(Guid customerId) => new() { BookingNumber = $"BK-{Guid.NewGuid():N}", CustomerId = customerId, BranchId = Branch.Id, Items = [new() { AssetId = Asset.Id, StartAt = DateTimeOffset.UtcNow.AddDays(30), EndAt = DateTimeOffset.UtcNow.AddDays(32), DailyRate = 100m }] };
        public PublicBookingRequest PublicRequest()
        {
            var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
            return new(Asset.Id, start, start.AddDays(2), "Spoofed", CustomerType.Individual, null, "spoofed@example.test", "1234567", "Suva", null, "Test hire", null, "Pickup", null, null, null, false, null, "Spoofed driver", "SPOOF-LICENCE", false, null);
        }
        public async ValueTask DisposeAsync() => await services.DisposeAsync();
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "CREMS.Api.Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
