using System.Globalization;
using System.Text.RegularExpressions;
using CREMS.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class RentalSettings
{
    public sealed record Definition(string Default, string Category, string Description);
    public static readonly IReadOnlyDictionary<string, Definition> Definitions = new Dictionary<string, Definition>
    {
        ["rentals.bookingPrefix"] = new("BK", "Numbering", "Prefix for new booking references. Existing references and quotation request numbers are unchanged."),
        ["rentals.agreementPrefix"] = new("RA", "Numbering", "Prefix for newly signed rental agreements. Existing agreements are unchanged."),
        ["rentals.vatRate"] = new("12.5", "Rental", "Saving applies this percentage to all divisions for new requests; existing bookings retain their tax rate."),
        ["rentals.lateFeeMultiplier"] = new("1", "Rental", "Multiplies the agreed daily rate prorated by overdue time. 0 disables calculated late fees; 1 is the normal rate."),
        ["security.lockoutMinutes"] = new("30", "Security", "Minutes for new failed-password lockouts. Existing lockout expiry times are unchanged."),
        ["security.maxFailedAttempts"] = new("5", "Security", "Failed password attempts before lockout. Applies on the next sign-in attempt."),
        ["data.retentionYears"] = new("7", "Compliance", "Years before closed rentals and audit records are flagged for retention review. Records are not automatically deleted."),
        ["reminders.quotationHours"] = new("24", "Notifications", "Send a quotation expiry reminder this many hours before expiry."),
        ["reminders.pickupHours"] = new("24", "Notifications", "Send a pickup reminder this many hours before collection."),
        ["reminders.invoiceDueDays"] = new("14", "Notifications", "Invoice payment term in days from issue, used for reminders when no due-date field is recorded."),
        ["reminders.documentDays"] = new("30", "Notifications", "Remind customers this many days before a document expires."),
        ["reminders.overdueReturnDays"] = new("14", "Notifications", "Maximum number of days to send overdue return reminders.")
    };

    public static string? Validate(string key, string? value)
    {
        if (!Definitions.ContainsKey(key)) return "This setting is not configurable here.";
        if (string.IsNullOrWhiteSpace(value)) return "Enter a value.";
        value = value.Trim();
        if (key.EndsWith("Prefix", StringComparison.Ordinal))
            return Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9-]{0,11}$") ? null : "Use 1–12 letters, numbers or hyphens, starting with a letter.";
        if (key == "rentals.vatRate" || key == "rentals.lateFeeMultiplier")
        {
            var max = key == "rentals.vatRate" ? 100m : 10m;
            return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) && number >= 0 && number <= max
                ? null : $"Enter a number from 0 to {max}.";
        }
        var limit = key == "security.lockoutMinutes" ? 1440 : key.StartsWith("reminders.", StringComparison.Ordinal) ? 365 : 100;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var integer) && integer >= 1 && integer <= limit
            ? null : $"Enter a whole number from 1 to {limit}.";
    }

    public static async Task<string> Read(ApplicationDbContext db, string key, CancellationToken token = default)
    {
        var value = await db.SystemSettings.AsNoTracking().Where(x => x.Key == key).Select(x => x.Value).FirstOrDefaultAsync(token);
        return Validate(key, value) is null ? value!.Trim() : Definitions[key].Default;
    }

    public static async Task<string> Reference(ApplicationDbContext db, bool agreement, CancellationToken token = default)
    {
        var prefix = await Read(db, agreement ? "rentals.agreementPrefix" : "rentals.bookingPrefix", token);
        return $"{prefix}-{DateTime.UtcNow:yyyy}-{Guid.NewGuid():N}".ToUpperInvariant();
    }

    public static async Task<IdentityOptions> LoginOptions(ApplicationDbContext db, IdentityOptions original, CancellationToken token = default)
    {
        // Identity's default options are shared. Copy before applying request-specific database settings.
        return new IdentityOptions
        {
            ClaimsIdentity = original.ClaimsIdentity, Password = original.Password,
            User = original.User, SignIn = original.SignIn, Tokens = original.Tokens, Stores = original.Stores,
            Lockout = new LockoutOptions
            {
                AllowedForNewUsers = original.Lockout.AllowedForNewUsers,
                MaxFailedAccessAttempts = int.Parse(await Read(db, "security.maxFailedAttempts", token), CultureInfo.InvariantCulture),
                DefaultLockoutTimeSpan = TimeSpan.FromMinutes(int.Parse(await Read(db, "security.lockoutMinutes", token), CultureInfo.InvariantCulture))
            }
        };
    }
}
