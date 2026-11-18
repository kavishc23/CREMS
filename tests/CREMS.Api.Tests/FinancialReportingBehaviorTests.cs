using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace CREMS.Api.Tests;
public sealed class FinancialReportingBehaviorTests
{
    private static CancellationToken Token=>TestContext.Current.CancellationToken;
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static async Task<(FinanceController Controller,Booking Booking,RentalInvoice Invoice,RentalPayment Payment,Asset Asset)> Setup(ApplicationDbContext db)
    {
        var (context,asset)=await MaintenanceJobsTests.Setup(db);
        var branch=await db.Branches.SingleAsync(Token);asset.Branch=branch;
        var division=new Division{Code="REPORT",Name="Reporting division"};db.Divisions.Add(division);asset.Division=division;asset.DivisionId=division.Id;
        var service=new ServiceOffering{DivisionId=division.Id,Code="REPORT",Name="Reporting hire",Type=ServiceOfferingType.EquipmentHire};db.ServiceOfferings.Add(service);asset.ServiceOffering=service;asset.ServiceOfferingId=service.Id;
        var category=new AssetCategory{Code="REPORT",Name="Reporting asset",DivisionId=division.Id};db.AssetCategories.Add(category);asset.AssetCategory=category;asset.AssetCategoryId=category.Id;
        var customer=new Customer{CustomerNumber="REPORT-C",Name="Reporting customer"};
        var booking=new Booking{BookingNumber="REPORT-B",BranchId=branch.Id,Branch=branch,CustomerId=customer.Id,Customer=customer,TaxRate=10,Status=BookingStatus.Completed};
        booking.Items.Add(new BookingItem{Asset=asset,AssetId=asset.Id,DailyRate=100,StartAt=new DateTimeOffset(2026,1,2,0,0,0,TimeSpan.FromHours(12)),EndAt=new DateTimeOffset(2026,1,3,0,0,0,TimeSpan.FromHours(12))});
        var invoice=new RentalInvoice{InvoiceNumber="REPORT-I",Booking=booking,BookingId=booking.Id,LineItemsJson="[]",Subtotal=100,TaxAmount=10,Total=110,BalanceDue=110,Status=InvoiceStatus.Issued,IssuedAt=new DateTimeOffset(2026,1,3,0,0,0,TimeSpan.FromHours(12))};
        var payment=new RentalPayment{Booking=booking,BookingId=booking.Id,Amount=50,Type=PaymentType.RentalCharge,Method=PaymentMethod.Cash,ReceiptNumber="REPORT-P",RecordedByName="Fixture"};
        db.AddRange(customer,booking,invoice,payment);using(db.SuppressNotifications())await db.SaveChangesAsync(Token);
        return(new FinanceController(db,new CurrentStaffScope(db)){ControllerContext=context.ControllerContext},booking,invoice,payment,asset);
    }
    [Fact]
    public async Task Invoice_allocation_credit_and_statement_reconcile_without_counting_bonds_as_revenue()
    {
        await using var db=MaintenanceJobsTests.Database();var(c,b,i,p,a)=await Setup(db);
        Assert.IsType<OkObjectResult>(await c.Allocate(p.Id,new(i.Id,50),Token));Assert.Equal(50,i.AmountPaid);Assert.Equal(60,i.BalanceDue);Assert.Equal(InvoiceStatus.PartiallyPaid,i.Status);
        Assert.IsType<BadRequestObjectResult>(await c.Allocate(p.Id,new(i.Id,1),Token));Assert.Single(await db.PaymentAllocations.ToListAsync(Token));
        Assert.IsType<OkObjectResult>(await c.Credit(i.Id,new("Approved adjustment",11),Token));Assert.Equal(99,i.Total);Assert.Equal(49,i.BalanceDue);
        var credit=await db.CreditNotes.SingleAsync(Token);Assert.Equal(10,credit.Subtotal);Assert.Equal(1,credit.TaxAmount);Assert.Equal(CreditNoteStatus.Applied,credit.Status);
        Assert.IsType<ConflictObjectResult>(await c.Lines(i.Id,[new("Changed",1,100,10,true)],Token));
        b.BondAmountHeld=100;db.RentalPayments.Add(new RentalPayment{BookingId=b.Id,Type=PaymentType.BondCollection,Method=PaymentMethod.Cash,Amount=100,ReceiptNumber="BOND",RecordedByName="Fixture"});using(db.SuppressNotifications())await db.SaveChangesAsync(Token);
        var summary=Json(Assert.IsType<OkObjectResult>(await c.Workspace(Token)).Value).GetProperty("summary");Assert.Equal(90,summary.GetProperty("revenue").GetDecimal());Assert.Equal(50,summary.GetProperty("received").GetDecimal());Assert.Equal(100,summary.GetProperty("depositsHeld").GetDecimal());
        var statement=Json(Assert.IsType<OkObjectResult>(await c.Statement(b.CustomerId,Token)).Value);Assert.Equal(99,statement.GetProperty("total").GetDecimal());Assert.Equal(49,statement.GetProperty("outstanding").GetDecimal());
        var reconciliation=Json(Assert.IsType<OkObjectResult>(await c.Reconciliation(b.Id,Token)).Value);Assert.Equal(50,reconciliation.GetProperty("allocatedPayments").GetDecimal());Assert.Equal(0,reconciliation.GetProperty("issues").GetArrayLength());
        var document=Json(Assert.IsType<OkObjectResult>(await c.InvoiceDocument(i.Id,Token)).Value);Assert.Equal("REPORT-I",document.GetProperty("invoiceNumber").GetString());
    }
    [Theory]
    [InlineData(false,100,10,110)] [InlineData(true,110,10,110)]
    public async Task Invoice_line_edit_calculates_tax_and_persists_document(bool inclusive,decimal price,decimal tax,decimal expected)
    {
        await using var db=MaintenanceJobsTests.Database();var(c,b,i,p,a)=await Setup(db);i.TaxInclusive=inclusive;
        Assert.IsType<OkObjectResult>(await c.Lines(i.Id,[new(" Hire ",1,price,10,true)],Token));Assert.Equal(expected,i.Total);Assert.Equal(tax,i.TaxAmount);Assert.Equal("Hire",Assert.Single(i.Lines).Description);Assert.Contains("Hire",i.LineItemsJson);
        Assert.IsType<BadRequestObjectResult>(await c.Lines(i.Id,[],Token));Assert.IsType<BadRequestObjectResult>(await c.Lines(i.Id,[new("Invalid",0,1,10,true)],Token));
        i.Status=InvoiceStatus.Paid;Assert.IsType<ConflictObjectResult>(await c.Lines(i.Id,[new("New",1,1,0,false)],Token));
    }
    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(111)] [InlineData(1.111)]
    public async Task Invalid_credits_leave_invoice_unchanged(decimal amount)
    {
        await using var db=MaintenanceJobsTests.Database();var(c,b,i,p,a)=await Setup(db);Assert.IsType<BadRequestObjectResult>(await c.Credit(i.Id,new("Adjustment",amount),Token));Assert.Equal(110,i.Total);Assert.Empty(await db.CreditNotes.ToListAsync(Token));
    }
    [Fact]
    public async Task Reporting_profit_and_utilization_respect_periods_and_asset_scope()
    {
        await using var db=MaintenanceJobsTests.Database();var(c,b,i,p,a)=await Setup(db);a.AcquisitionDate=new DateOnly(2026,1,1);
        db.AssetCostEntries.Add(new AssetCostEntry{AssetId=a.Id,BranchId=a.BranchId,Description="Cleaning",Amount=20,OccurredOn=new DateOnly(2026,1,3),RecordedByName="Fixture"});using(db.SuppressNotifications())await db.SaveChangesAsync(Token);
        var from=new DateOnly(2026,1,1);var to=new DateOnly(2026,1,31);
        var profit=Json(await PeriodProfitabilityReport.Build(db,db.Assets,from,to,Token));Assert.Equal(100,profit.GetProperty("totals").GetProperty("revenue").GetDecimal());Assert.Equal(20,profit.GetProperty("totals").GetProperty("expense").GetDecimal());Assert.Equal(80,profit.GetProperty("totals").GetProperty("profit").GetDecimal());
        Assert.Equal(1,profit.GetProperty("assets").GetArrayLength());Assert.Equal(1,profit.GetProperty("monthlyTrend").GetArrayLength());Assert.Equal(1,profit.GetProperty("branchTrend").GetArrayLength());
        var usage=Json(await HistoricalUtilizationReport.Build(db,db.Assets,from,to,Token));var row=usage.GetProperty("assets")[0];Assert.Equal(31,row.GetProperty("capacityDays").GetDecimal());Assert.Equal(1,row.GetProperty("hiredDays").GetDecimal());Assert.Equal(30,row.GetProperty("availableDays").GetDecimal());
        var exceptions=Json(await ReportingExceptionsReport.Build(db,db.Assets,from,to,Token));Assert.Contains("Unpaid invoice",exceptions.GetRawText());Assert.Contains("Unallocated payment",exceptions.GetRawText());
        var reporting=new ReportingController(db,new CurrentStaffScope(db)){ControllerContext=c.ControllerContext};var workspace=Json(Assert.IsType<OkObjectResult>(await reporting.Workspace(from,to,null,null,null,null,null,null,null,Token)).Value);Assert.Equal(1,workspace.GetProperty("assets").GetArrayLength());Assert.Equal(100,workspace.GetProperty("summary").GetProperty("revenue").GetDecimal());
        Assert.IsType<BadRequestObjectResult>(await reporting.Workspace(to,from,null,null,null,null,null,null,null,Token));Assert.IsType<NotFoundResult>(await reporting.Workspace(from,to,null,null,Guid.NewGuid(),null,null,null,null,Token));
        var empty=Json(await PeriodProfitabilityReport.Build(db,db.Assets.Where(x=>false),from,to,Token));Assert.Equal(0,empty.GetProperty("totals").GetProperty("profit").GetDecimal());
    }
    [Fact]
    public async Task Finance_requires_a_session_and_unknown_references_return_not_found()
    {
        await using var db=MaintenanceJobsTests.Database();var(c,b,i,p,a)=await Setup(db);
        Assert.IsType<NotFoundResult>(await c.InvoiceDocument(Guid.NewGuid(),Token));Assert.IsType<NotFoundResult>(await c.Lines(Guid.NewGuid(),[],Token));Assert.IsType<NotFoundResult>(await c.Credit(Guid.NewGuid(),new("Reason",10),Token));Assert.IsType<NotFoundResult>(await c.Allocate(Guid.NewGuid(),new(i.Id,10),Token));
        c.ControllerContext=new(){HttpContext=new Microsoft.AspNetCore.Http.DefaultHttpContext()};Assert.IsType<ForbidResult>(await c.Workspace(Token));Assert.IsType<ForbidResult>(await c.InvoiceDocument(i.Id,Token));Assert.IsType<ForbidResult>(await c.Reconciliation(b.Id,Token));Assert.IsType<ForbidResult>(await c.Statement(b.CustomerId,Token));
    }
}
