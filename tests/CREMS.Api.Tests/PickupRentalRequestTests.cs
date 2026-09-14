using System.Text.Json;
using CREMS.Api.Controllers;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class PickupRentalRequestTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("\"2027-12-31\"")]
    public void Pickup_accepts_optional_licence_expiry(string expiry)
    {
        var request = JsonSerializer.Deserialize<PickupRentalRequest>(
            "{\"licenceNumber\":null,\"licenceExpiry\":" + expiry + "}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(request);
        Assert.Equal(expiry == "null" ? (DateOnly?)null : new DateOnly(2027, 12, 31), request.LicenceExpiry);
    }
}
