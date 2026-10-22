using CREMS.Api.Data;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class CustomerLicenceConfirmationTests
{
    [Fact]
    public async Task Scanned_fields_cannot_be_changed_during_confirmation()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var customer = new Customer { CustomerNumber = "TEST-1", Name = "RAHUL CHAND" };
        var submission = new CustomerLicence
        {
            CustomerId = customer.Id, StorageKey = "test.png", ContentType = "image/png", ContentHash = "test",
            ExtractedName = customer.Name, LicenceNumber = "1060343", LicenceClasses = "2,3", OcrStatus = LicenceOcrStatus.Completed
        };
        db.Customers.Add(customer);
        db.CustomerLicences.Add(submission);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new CustomerLicenceService(db, null!, null!);

        var failure = await Assert.ThrowsAsync<LicenceException>(() => service.ConfirmAsync(customer.Id, submission.Id,
            "9999999", [3], null, requireNameMatch: true, TestContext.Current.CancellationToken));

        Assert.Equal("scan_details_changed", failure.Code);
        Assert.Equal(LicenceVerificationStatus.Required, submission.Status);
        Assert.Equal("1060343", submission.LicenceNumber);
        Assert.Equal("2,3", submission.LicenceClasses);
    }
}
