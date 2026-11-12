using CREMS.Api.Controllers;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class FinalValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("data:text/html;base64,PGgxPmJhZDwvaDE+")]
    [InlineData("data:image/png;base64,not-base64")]
    [InlineData("data:application/pdf;base64,aW52YWxpZA==")]
    public void Invalid_incident_attachment_is_rejected(string? file)
        => Assert.NotNull(IncidentEvidence.Validate(new[] { file! }));

    [Fact]
    public void Incident_attachment_limits_and_valid_file_headers_are_enforced()
    {
        const string pdf="data:application/pdf;base64,JVBERi0xLjQK";
        Assert.Null(IncidentEvidence.Validate(new[] {pdf}));
        Assert.NotNull(IncidentEvidence.Validate(Enumerable.Repeat(pdf,6).ToArray()));
        Assert.NotNull(IncidentEvidence.Validate(new[]{"data:image/png;base64,"+new string('A',2_800_001)}));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(365)]
    public async Task Passing_completion_requires_future_date_and_updates_only_passing_expiry(int? offset)
    {
        await using var db=MaintenanceJobsTests.Database();
        var (controller,asset)=await MaintenanceJobsTests.Setup(db);
        var token=TestContext.Current.CancellationToken;
        var today=MaintenanceRules.LocalDate(DateTimeOffset.UtcNow);
        asset.NextServiceDate=today.AddDays(60);
        await controller.Create(new(asset.Id,"Vehicle passing","Annual roadworthiness check",null,null,0,null),token);
        var job=await db.MaintenanceJobs.SingleAsync(token);
        var date=offset.HasValue?today.AddDays(offset.Value):(DateOnly?)null;
        var result=await controller.Update(job.Id,MaintenanceJobsTests.Request(MaintenanceStatus.Completed) with {ServiceType="Vehicle passing",NextServiceDate=date},token);
        if(offset is >0){Assert.IsType<NoContentResult>(result);Assert.Equal(date,asset.PassingExpiry);}
        else {Assert.IsType<BadRequestObjectResult>(result);Assert.Null(asset.PassingExpiry);Assert.Equal(MaintenanceStatus.Open,job.Status);}
        Assert.Equal(today.AddDays(60),asset.NextServiceDate);
    }
}
