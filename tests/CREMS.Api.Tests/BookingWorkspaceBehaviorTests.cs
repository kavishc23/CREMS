using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace CREMS.Api.Tests;
public sealed class BookingWorkspaceBehaviorTests
{
    private static CancellationToken T=>TestContext.Current.CancellationToken;
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static async Task<(BookingsController C,Booking B,Asset A)> Setup(ApplicationDbContext db,BookingStatus status=BookingStatus.Draft)
    {
        var(ctx,a)=await MaintenanceJobsTests.Setup(db);var branch=await db.Branches.SingleAsync(T);a.Branch=branch;a.Type=AssetType.LightEquipment;a.Status=AssetStatus.Available;
        var customer=new Customer{CustomerNumber="WORK-C",Name="Workspace customer",Email="training@example.test"};
        var b=new Booking{BookingNumber="WORK-B",Branch=branch,BranchId=branch.Id,Customer=customer,CustomerId=customer.Id,Status=status,TaxRate=10,DepositRequired=100,Items=[new(){Asset=a,AssetId=a.Id,StartAt=DateTimeOffset.UtcNow.AddDays(3),EndAt=DateTimeOffset.UtcNow.AddDays(4),DailyRate=100,HireQuantity=1}]};
        db.AddRange(customer,b);using(db.SuppressNotifications())await db.SaveChangesAsync(T);
        return(new BookingsController(db,new CurrentStaffScope(db)){ControllerContext=ctx.ControllerContext},b,a);
    }
    private static UpdateBookingRequest Edit(Booking b,Asset a)=>new(b.BranchId,b.CustomerId,a.Id,b.Items.Single().StartAt,b.Items.Single().EndAt,150," Updated notes ",5,10,100,20," Delivery ");
    [Fact] public async Task Workspace_and_filtered_list_include_customer_financial_and_readiness_details()
    {
        await using var db=MaintenanceJobsTests.Database();var(c,b,a)=await Setup(db);
        var w=Json(Assert.IsType<OkObjectResult>(await c.GetWorkspace(b.Id,T)).Value);
        Assert.Equal("WORK-B",w.GetProperty("bookingNumber").GetString());Assert.Equal("Workspace customer",w.GetProperty("customer").GetProperty("name").GetString());Assert.Equal(110,w.GetProperty("pricing").GetProperty("total").GetDecimal());Assert.False(w.GetProperty("readiness").GetProperty("confirmed").GetBoolean());
        var list=Assert.IsAssignableFrom<IReadOnlyList<BookingResponse>>(Assert.IsType<OkObjectResult>((await c.GetAll(BookingStatus.Draft,T)).Result).Value);Assert.Equal(b.Id,Assert.Single(list).Id);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<BookingResponse>>(Assert.IsType<OkObjectResult>((await c.GetAll(BookingStatus.Completed,T)).Result).Value));
    }
    [Fact] public async Task Edit_persists_prices_and_trims_notes_without_changing_identity()
    {
        await using var db=MaintenanceJobsTests.Database();var(c,b,a)=await Setup(db);var result=Assert.IsType<OkObjectResult>((await c.Update(b.Id,Edit(b,a),T)).Result);var value=Assert.IsType<BookingResponse>(result.Value);Assert.Equal(b.Id,value.Id);Assert.Equal("Updated notes",value.Notes);Assert.Equal(150,Assert.Single(value.Items).DailyRate);Assert.Equal("Delivery",b.AdditionalChargesDescription);Assert.Contains(await db.AuditEvents.ToListAsync(T),x=>x.Action=="Booking updated");
    }
    [Theory] [InlineData(BookingStatus.ConvertedToRental)] [InlineData(BookingStatus.Completed)]
    public async Task Active_and_completed_rentals_cannot_be_reassigned(BookingStatus status)
    {await using var db=MaintenanceJobsTests.Database();var(c,b,a)=await Setup(db,status);Assert.IsType<BadRequestObjectResult>((await c.Update(b.Id,Edit(b,a),T)).Result);Assert.Equal(100,b.Items.Single().DailyRate);}
    [Theory] [InlineData(AssetStatus.Maintenance)] [InlineData(AssetStatus.Inspection)] [InlineData(AssetStatus.OutOfService)] [InlineData(AssetStatus.Retired)]
    public async Task Edit_rejects_nonoperational_assets(AssetStatus status)
    {await using var db=MaintenanceJobsTests.Database();var(c,b,a)=await Setup(db);a.Status=status;await db.SaveChangesAsync(T);Assert.IsType<BadRequestObjectResult>((await c.Update(b.Id,Edit(b,a),T)).Result);Assert.Equal(100,b.Items.Single().DailyRate);}
    [Theory] [InlineData(-1,0)] [InlineData(0,-1)] [InlineData(0,101)]
    public async Task Edit_rejects_invalid_financial_values(decimal rate,decimal vat)
    {await using var db=MaintenanceJobsTests.Database();var(c,b,a)=await Setup(db);Assert.IsType<BadRequestObjectResult>((await c.Update(b.Id,Edit(b,a) with{DailyRate=rate,TaxRate=vat},T)).Result);Assert.Equal(100,b.Items.Single().DailyRate);}
    [Theory] [InlineData(0,BondStatus.NotRequired)] [InlineData(50,BondStatus.Held)] [InlineData(100,BondStatus.AwaitingPayment)]
    public async Task Bond_updates_preserve_held_funds_and_record_the_correct_state(decimal amount,BondStatus expected)
    {await using var db=MaintenanceJobsTests.Database();var(c,b,a)=await Setup(db,BookingStatus.Confirmed);b.BondAmountHeld=amount==0?0:50;await db.SaveChangesAsync(T);Assert.IsType<OkObjectResult>(await c.UpdateBond(b.Id,new(amount),T));Assert.Equal(amount,b.DepositRequired);Assert.Equal(expected,b.BondStatus);Assert.Contains(await db.AuditEvents.ToListAsync(T),x=>x.Action=="Refundable bond updated");}
    [Theory] [InlineData(-1)] [InlineData(1000001)] [InlineData(49)]
    public async Task Invalid_bond_amounts_cannot_reduce_held_money(decimal amount)
    {await using var db=MaintenanceJobsTests.Database();var(c,b,a)=await Setup(db,BookingStatus.Confirmed);b.BondAmountHeld=50;await db.SaveChangesAsync(T);Assert.IsType<BadRequestObjectResult>(await c.UpdateBond(b.Id,new(amount),T));Assert.Equal(100,b.DepositRequired);}
    [Fact] public async Task Confirmed_exception_cancellation_releases_asset_and_creates_customer_resolution()
    {await using var db=MaintenanceJobsTests.Database();var(c,b,a)=await Setup(db,BookingStatus.Confirmed);a.Status=AssetStatus.Reserved;await db.SaveChangesAsync(T);Assert.IsType<BadRequestObjectResult>(await c.ResolveConfirmedException(b.Id,new(null,"short"),T));Assert.IsType<OkObjectResult>(await c.ResolveConfirmedException(b.Id,new(null,"Customer agreed to cancellation after breakdown."),T));Assert.Equal(BookingStatus.Cancelled,b.Status);Assert.Equal(AssetStatus.Available,a.Status);var customerCase=await db.CustomerCases.SingleAsync(T);Assert.Equal(CaseStatus.Closed,customerCase.Status);Assert.Contains("breakdown",customerCase.Resolution);Assert.Contains("Confirmed hire cancelled",b.Notes);}
    [Fact] public async Task Held_bond_prevents_cancellation_without_mutation()
    {await using var db=MaintenanceJobsTests.Database();var(c,b,a)=await Setup(db,BookingStatus.Confirmed);b.BondAmountHeld=100;await db.SaveChangesAsync(T);Assert.IsType<ConflictObjectResult>(await c.ResolveConfirmedException(b.Id,new(null,"Customer agreed to cancellation after breakdown."),T));Assert.Equal(BookingStatus.Confirmed,b.Status);Assert.Empty(await db.CustomerCases.ToListAsync(T));}
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Customer_cancellation_decision_updates_request_and_booking_consistently(bool approved)
    {await using var db=MaintenanceJobsTests.Database();var(c,b,a)=await Setup(db,BookingStatus.Confirmed);var request=new CustomerCase{CaseNumber="CASE-1",CustomerId=b.CustomerId,BranchId=b.BranchId,Subject=$"Cancellation request {b.BookingNumber}",Description="Customer requests cancellation."};db.CustomerCases.Add(request);await db.SaveChangesAsync(T);Assert.IsType<OkObjectResult>(await c.DecideCustomerRequest(b.Id,request.Id,new(approved," Reviewed with customer "),T));Assert.Equal(approved?CaseStatus.Resolved:CaseStatus.Closed,request.Status);Assert.Equal(approved?BookingStatus.Cancelled:BookingStatus.Confirmed,b.Status);Assert.Equal("Reviewed with customer",request.Resolution);Assert.IsType<BadRequestObjectResult>(await c.DecideCustomerRequest(b.Id,request.Id,new(true,null),T));}
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Approved_extension_accepts_exact_or_legacy_requested_dates(bool legacy)
    {await using var db=MaintenanceJobsTests.Database();var(c,b,a)=await Setup(db,BookingStatus.Confirmed);var end=b.Items.Single().EndAt.AddDays(2);var request=new CustomerCase{CaseNumber="EXT-1",CustomerId=b.CustomerId,BranchId=b.BranchId,Subject=$"Extension request {b.BookingNumber}",Description=legacy?$"Requested new return: {end:u}. Reason: More time":$"RequestedEndAt: {end:O}"};db.CustomerCases.Add(request);await db.SaveChangesAsync(T);Assert.IsType<OkObjectResult>(await c.DecideCustomerRequest(b.Id,request.Id,new(true,null),T));Assert.Equal(end.ToUnixTimeSeconds(),b.Items.Single().EndAt.ToUnixTimeSeconds());Assert.Equal(CaseStatus.Resolved,request.Status);Assert.Contains("Extension approved",b.Notes);}
    [Fact] public async Task Unknown_bookings_return_not_found_and_anonymous_users_are_forbidden()
    {await using var db=MaintenanceJobsTests.Database();var(c,b,a)=await Setup(db);var id=Guid.NewGuid();Assert.IsType<NotFoundResult>(await c.GetWorkspace(id,T));Assert.IsType<NotFoundResult>(await c.GetReplacementAssets(id,T));Assert.IsType<NotFoundResult>(await c.UpdateBond(id,new(1),T));Assert.IsType<NotFoundResult>(await c.ResolveConfirmedException(id,new(null,"Enough explanation"),T));Assert.IsType<NotFoundResult>((await c.Update(id,Edit(b,a),T)).Result);Assert.IsType<NotFoundResult>(await c.DecideCustomerRequest(id,id,new(true,null),T));c.ControllerContext=new(){HttpContext=new Microsoft.AspNetCore.Http.DefaultHttpContext()};Assert.IsType<ForbidResult>(await c.GetWorkspace(b.Id,T));Assert.IsType<ForbidResult>((await c.GetAll(null,T)).Result);Assert.IsType<ForbidResult>(await c.UpdateBond(b.Id,new(1),T));}
    [Fact] public async Task Quotation_creation_and_revision_keep_history_and_recalculate_booking_prices()
    {await using var db=MaintenanceJobsTests.Database();var(c,b,a)=await Setup(db);var request=new PrepareBookingQuotationRequest(DateTimeOffset.UtcNow.AddDays(7),100,5,10,"Initial estimate",[new("Equipment hire",1,100,0,ChargeUnit.Day,ChargeCategory.BaseHire),new("Delivery",1,20,10,ChargeUnit.Unit,ChargeCategory.Transport)]);Assert.IsType<OkObjectResult>(await c.PrepareQuotation(b.Id,request,T));var quote=await db.SalesQuotes.SingleAsync(T);Assert.Equal(126.5m,quote.Total);Assert.Equal(20,b.AdditionalCharges);Assert.Equal(100,b.Items.Single().DailyRate);Assert.Equal(10,Assert.Single(await db.BookingCharges.ToListAsync(T)).UnitCost);Assert.IsType<OkObjectResult>(await c.PrepareQuotation(b.Id,request with {Discount=10,RevisionReason="Agreed customer discount"},T));Assert.Equal(2,quote.Version);Assert.Equal(121,quote.Total);var revision=await db.QuoteRevisions.SingleAsync(T);Assert.Contains("126.5",revision.SnapshotJson);Assert.Equal("Agreed customer discount",revision.ChangeReason);}
    [Theory] [InlineData("NewRequests")] [InlineData("QuotationRequired")] [InlineData("AwaitingApproval")] [InlineData("Confirmed")] [InlineData("Closed")]
    public async Task Work_queue_classifies_and_searches_references_across_stages(string queue)
    {await using var db=MaintenanceJobsTests.Database();var(c,b,a)=await Setup(db);var result=Assert.IsType<BookingWorkQueueResponse>(Assert.IsType<OkObjectResult>((await c.GetWorkQueue(queue,"WORK-B",0,500,T)).Result).Value);var item=Assert.Single(result.Items);Assert.Equal(b.Id,item.Id);Assert.Equal("QuotationRequired",item.Queue);Assert.Equal(1,result.Page);Assert.Equal(100,result.PageSize);Assert.Equal(100,item.EstimatedValue);Assert.Equal(1,result.Counts.QuotationRequired);var empty=Assert.IsType<BookingWorkQueueResponse>(Assert.IsType<OkObjectResult>((await c.GetWorkQueue(queue,"missing",1,10,T)).Result).Value);Assert.Empty(empty.Items);}
    [Fact] public async Task Charges_are_replaced_and_removed_with_persisted_totals()
    {await using var db=MaintenanceJobsTests.Database();var(c,b,a)=await Setup(db);Assert.IsType<OkObjectResult>(await c.ReplaceCharges(b.Id,[new(null,null," Cleaning ",ChargeCategory.Other,ChargeUnit.Unit,2,10,3,true)],T));Assert.Equal(20,b.AdditionalCharges);Assert.Equal("Cleaning",Assert.Single(await db.BookingCharges.ToListAsync(T)).Description);Assert.IsType<OkObjectResult>(await c.ReplaceCharges(b.Id,[],T));Assert.Equal(0,b.AdditionalCharges);Assert.Null(b.AdditionalChargesDescription);Assert.Empty(await db.BookingCharges.ToListAsync(T));}

}
