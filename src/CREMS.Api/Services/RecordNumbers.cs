using CREMS.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class RecordNumbers
{
    // Call inside the save transaction. The database lock also covers registrations
    // from the customer website and concurrent API instances.
    public static async Task<string> NextAsync(ApplicationDbContext db, string kind, CancellationToken token)
    {
        if (db.Database.IsSqlServer())
        {
            var resource = $"CREMS.RecordNumber.{kind}";
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource = {resource},
                    @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
                IF @result < 0 THROW 51000, 'The record number could not be reserved. Please retry.', 1;", token);
        }
        var numbers = kind == "Customer"
            ? await db.Customers.AsNoTracking().Select(item => item.CustomerNumber).ToListAsync(token)
            : await db.Suppliers.AsNoTracking().Select(item => item.SupplierNumber).ToListAsync(token);
        var prefix = kind == "Customer" ? "CUS-" : "SUP-";
        var next = numbers.Select(number => number.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(number.AsSpan(prefix.Length), out var value) ? value : 0).DefaultIfEmpty().Max() + 1;
        return $"{prefix}{next:D6}";
    }
}

