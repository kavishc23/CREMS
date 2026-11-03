using CREMS.Api.Data;using CREMS.Api.Domain.Identity;using CREMS.Api.Domain.Rentals;using CREMS.Api.Services;using Microsoft.AspNetCore.Authorization;using Microsoft.AspNetCore.Mvc;using Microsoft.EntityFrameworkCore;
namespace CREMS.Api.Controllers;
[ApiController,Route("api/finance"),Authorize(Policy=SystemPolicies.ManageFinance)] public sealed class FinanceController(ApplicationDbContext db,CurrentStaffScope staffScope):ControllerBase
{
 [HttpGet("invoices/{id:guid}/document")]
 public async Task<ActionResult> InvoiceDocument(Guid id, CancellationToken token)
 {
  var invoice = await db.RentalInvoices.AsNoTracking().Include(x => x.Lines).Include(x => x.Booking).ThenInclude(x => x!.Customer)
   .Include(x => x.Booking).ThenInclude(x => x!.Branch).FirstOrDefaultAsync(x => x.Id == id, token);
  if (invoice?.Booking is null) return NotFound();
  var scope = await staffScope.GetAsync(User);
  if (scope is null || !scope.HasBranchAccess(invoice.Booking.BranchId)) return Forbid();
  return Ok(new { invoice.Id, invoice.InvoiceNumber, invoice.Booking.BookingNumber, customerName = invoice.Booking.Customer?.Name,
   branchName = invoice.Booking.Branch?.Name, invoice.Subtotal, invoice.TaxAmount, invoice.TaxInclusive, invoice.Total, invoice.AmountPaid, invoice.BalanceDue,
   status = invoice.Status.ToString(), invoice.IssuedAt, lines = InvoiceDocumentLines.Read(invoice),
   adjustment = RentalFinancials.Money(invoice.Total - (invoice.TaxInclusive ? invoice.Subtotal : invoice.Subtotal + invoice.TaxAmount)) });
 }
 [HttpGet("workspace")]
 public async Task<ActionResult> Workspace(CancellationToken token)
 {
  var scope=await staffScope.GetAsync(User);if(scope is null)return Forbid();
  var invoices=db.RentalInvoices.AsNoTracking().Where(x=>x.Booking!=null&&(scope.IsAdministrator||scope.BranchIds.Contains(x.Booking.BranchId)));
  var payments=db.RentalPayments.AsNoTracking().Where(x=>x.Booking!=null&&(scope.IsAdministrator||scope.BranchIds.Contains(x.Booking.BranchId)));
  var allInvoices=await invoices.ToListAsync(token);
  var invoiceIds=allInvoices.Select(x=>x.Id).ToArray();
  var credits=await db.CreditNotes.AsNoTracking().Where(x=>invoiceIds.Contains(x.InvoiceId)&&x.Status==CreditNoteStatus.Applied).ToListAsync(token);
  var creditByInvoice=credits.GroupBy(x=>x.InvoiceId).ToDictionary(x=>x.Key,x=>x.Sum(y=>y.Subtotal));
  var invoiceRows=await invoices.OrderByDescending(x=>x.IssuedAt).Take(200).Select(x=>new{x.Id,x.InvoiceNumber,x.BookingId,BookingNumber=x.Booking!.BookingNumber,CustomerName=x.Booking.Customer!.Name,CustomerId=x.Booking.CustomerId,BranchName=x.Booking.Branch!.Name,x.Subtotal,x.TaxAmount,x.Total,x.AmountPaid,x.BalanceDue,Status=x.Status.ToString(),x.IssuedAt}).ToListAsync(token);
  var invoiceDisplay=invoiceRows.Select(x=>new{x.Id,x.InvoiceNumber,x.BookingId,x.BookingNumber,x.CustomerName,x.CustomerId,x.BranchName,x.Subtotal,x.TaxAmount,x.Total,x.AmountPaid,x.BalanceDue,x.Status,x.IssuedAt,creditSubtotal=creditByInvoice.GetValueOrDefault(x.Id)}).ToArray();
  var paymentRows=await payments.OrderByDescending(x=>x.CreatedAt).Take(200).Select(x=>new{x.Id,x.BookingId,BookingNumber=x.Booking!.BookingNumber,CustomerName=x.Booking.Customer!.Name,BranchName=x.Booking.Branch!.Name,Type=x.Type.ToString(),Method=x.Method.ToString(),x.Amount,x.ReceiptNumber,Status=x.Status.ToString(),x.CreatedAt,x.RecordedByName}).ToListAsync(token);
  var cashReceived = await payments.Where(x => x.Status == PaymentStatus.Recorded && (x.Type == PaymentType.RentalCharge || x.Type == PaymentType.AdditionalCharge)).SumAsync(x => (decimal?)x.Amount, token) ?? 0;
  var heldBonds = await db.Bookings.AsNoTracking().Where(x => scope.IsAdministrator || scope.BranchIds.Contains(x.BranchId))
   .SumAsync(x => (decimal?)(x.BondAmountHeld - x.BondRefundAmount - (x.BondSettledAt != null ? x.BondDeductionAmount : 0)), token) ?? 0;
  return Ok(new{summary=new{revenue=ReportingKpis.Revenue(allInvoices,credits),invoiced=ReportingKpis.GrossInvoiced(allInvoices),received=cashReceived,outstanding=ReportingKpis.Outstanding(allInvoices),unpaidInvoices=allInvoices.Count(x=>x.Status!=InvoiceStatus.Voided&&x.BalanceDue>0),depositsHeld=Math.Max(0,heldBonds)},invoices=invoiceDisplay,payments=paymentRows});
 }
 [HttpGet("bookings/{bookingId:guid}/reconciliation")]
 public async Task<ActionResult> Reconciliation(Guid bookingId,CancellationToken token)
 {
  var scope=await staffScope.GetAsync(User);if(scope is null)return Forbid();
  var booking=await db.Bookings.AsNoTracking().Include(x=>x.Customer).Include(x=>x.Branch).Include(x=>x.Items).ThenInclude(x=>x.Asset)
   .Include(x=>x.Charges).Include(x=>x.Invoice).ThenInclude(x=>x!.Lines).Include(x=>x.Payments)
   .FirstOrDefaultAsync(x=>x.Id==bookingId,token);
  if(booking is null)return NotFound();
  if(!scope.HasBranchAccess(booking.BranchId))return Forbid();
  var quote=await db.SalesQuotes.AsNoTracking().Where(x=>x.ConvertedBookingId==bookingId).OrderByDescending(x=>x.CreatedAt)
   .Select(x=>new{x.Id,x.QuoteNumber,Status=x.Status.ToString(),x.Subtotal,x.Discount,x.Tax,x.Total}).FirstOrDefaultAsync(token);
  var invoice=booking.Invoice;
  var allocations=invoice is null?[]:await db.PaymentAllocations.AsNoTracking().Where(x=>x.InvoiceId==invoice.Id)
   .Select(x=>new{x.PaymentId,x.Amount}).ToListAsync(token);
  var credits=invoice is null?[]:await db.CreditNotes.AsNoTracking().Where(x=>x.InvoiceId==invoice.Id)
   .Select(x=>new{x.CreditNoteNumber,x.Reason,x.Total,Status=x.Status.ToString()}).ToListAsync(token);
  var invoiceTotal=invoice?.Total??0m;var allocated=allocations.Sum(x=>x.Amount);
  var recorded=booking.Payments.Where(x=>x.Status==PaymentStatus.Recorded).Sum(x=>x.Amount);
  var issues=new List<string>();
  if(invoice is not null)
  {
   if(Math.Abs(invoiceTotal-invoice.AmountPaid-invoice.BalanceDue)>0.01m)issues.Add("Invoice total does not equal paid plus balance.");
   if(allocated>recorded+0.01m)issues.Add("Allocations exceed recorded payments.");
   if(allocated>invoice.AmountPaid+0.01m)issues.Add("Allocations exceed the invoice paid amount.");
   if(invoice.Status==InvoiceStatus.Paid&&invoice.BalanceDue>0.01m)issues.Add("Invoice is marked paid but has a balance.");
  }
  return Ok(new{booking=new{booking.Id,booking.BookingNumber,status=booking.Status.ToString(),customerName=booking.Customer?.Name,branchName=booking.Branch?.Name},
   quote,charges=booking.Charges.Select(x=>new{x.Description,category=x.Category.ToString(),x.Quantity,x.UnitRate,total=x.Quantity*x.UnitRate}),
   invoice=invoice is null?null:new{invoice.Id,invoice.InvoiceNumber,status=invoice.Status.ToString(),invoice.Subtotal,invoice.TaxAmount,invoice.Total,invoice.AmountPaid,invoice.BalanceDue,
    lines=invoice.Lines.Select(x=>new{x.Description,x.Quantity,x.UnitPrice,total=x.Quantity*x.UnitPrice})},
   payments=booking.Payments.OrderBy(x=>x.CreatedAt).Select(x=>new{x.Id,x.ReceiptNumber,type=x.Type.ToString(),status=x.Status.ToString(),x.Amount,x.CreatedAt}),
   allocations,creditNotes=credits,recordedPayments=recorded,allocatedPayments=allocated,issues});
 }
 [HttpPut("invoices/{id:guid}/lines")]
 public async Task<ActionResult> Lines(Guid id, IReadOnlyList<InvoiceLineRequest> lines, CancellationToken token)
 {
  await using var transaction = await MaintenanceRules.BeginAsync(db, token);
  var invoice = await db.RentalInvoices.Include(x => x.Booking).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, token);
  if (invoice is null) return NotFound();
  var scope = await staffScope.GetAsync(User);
  if (scope is null || !scope.HasBranchAccess(invoice.Booking!.BranchId)) return Forbid();
  if (invoice.Status is InvoiceStatus.Paid or InvoiceStatus.Voided) return Conflict(new { message = "Paid or voided invoices cannot be edited." });
  if (lines.Count == 0 || lines.Any(x => string.IsNullOrWhiteSpace(x.Description) || x.Quantity <= 0 || x.UnitPrice < 0 || x.TaxRate is < 0 or > 100))
   return BadRequest(new { message = "Add invoice lines with descriptions, positive quantities, non-negative prices and valid VAT rates. Use credit notes for reductions." });
  if (await db.CreditNotes.AnyAsync(x => x.InvoiceId == id && x.Status == CreditNoteStatus.Applied, token))
   return Conflict(new { message = "Use a credit note to adjust an invoice that already has applied credits." });
  var subtotal = lines.Sum(x => RentalFinancials.Money(x.Quantity * x.UnitPrice));
  var tax = RentalFinancials.Money(lines.Where(x => x.IsTaxable).Sum(x => invoice.TaxInclusive
   ? RentalFinancials.Money(x.Quantity * x.UnitPrice) * x.TaxRate / (100 + x.TaxRate)
   : RentalFinancials.Money(x.Quantity * x.UnitPrice) * x.TaxRate / 100));
  var total = invoice.TaxInclusive ? subtotal : subtotal + tax;
  if (total < invoice.AmountPaid) return BadRequest(new { message = "The revised total cannot be below payments already applied. Resolve the refund or credit first." });
  db.InvoiceLines.RemoveRange(invoice.Lines);
  invoice.Lines = lines.Select(x => new InvoiceLine { Description = x.Description.Trim(), Quantity = x.Quantity, UnitPrice = x.UnitPrice, TaxRate = x.TaxRate, IsTaxable = x.IsTaxable }).ToList();
  invoice.Subtotal = subtotal; invoice.TaxAmount = tax; invoice.Total = total;
  invoice.LineItemsJson = System.Text.Json.JsonSerializer.Serialize(invoice.Lines.Select(x => new { x.Description, x.Quantity, x.UnitPrice, x.TaxRate, x.IsTaxable }));
  invoice.BalanceDue = total - invoice.AmountPaid;
  invoice.Status = invoice.BalanceDue == 0 ? InvoiceStatus.Paid : invoice.AmountPaid > 0 ? InvoiceStatus.PartiallyPaid : InvoiceStatus.Issued;
  await db.SaveChangesAsync(token);
  if (transaction is not null) await transaction.CommitAsync(token);
  return Ok(InvoiceResponse(invoice));
 }
 [HttpPost("payments/{paymentId:guid}/allocate")]
 public async Task<ActionResult> Allocate(Guid paymentId, AllocationRequest request, CancellationToken token)
 {
  await using var transaction = await MaintenanceRules.BeginAsync(db, token);
  var payment = await db.RentalPayments.Include(x => x.Booking).FirstOrDefaultAsync(x => x.Id == paymentId, token);
  var invoice = await db.RentalInvoices.FirstOrDefaultAsync(x => x.Id == request.InvoiceId, token);
  if (payment is null || invoice is null || payment.BookingId != invoice.BookingId) return NotFound();
  var scope = await staffScope.GetAsync(User);
  if (scope is null || !scope.HasBranchAccess(payment.Booking!.BranchId)) return Forbid();
  if (payment.Status != PaymentStatus.Recorded || payment.Type is not (PaymentType.RentalCharge or PaymentType.AdditionalCharge) || invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Voided)
   return BadRequest(new { message = "Only recorded hire payments can be allocated to an issued invoice. Bonds and refunds are settled separately." });
  var used = await db.PaymentAllocations.Where(x => x.PaymentId == paymentId).SumAsync(x => (decimal?)x.Amount, token) ?? 0;
  var allocated = await db.PaymentAllocations.Where(x => x.InvoiceId == invoice.Id).SumAsync(x => (decimal?)x.Amount, token) ?? 0;
  // Historic return flows included receipts in AmountPaid before allocation records existed.
  var alreadyIncluded = Math.Max(0, invoice.AmountPaid - allocated - (payment.Booking.BondSettledAt.HasValue ? payment.Booking.BondDeductionAmount : 0));
  var newCredit = Math.Max(0, request.Amount - alreadyIncluded);
  if (request.Amount <= 0 || request.Amount != RentalFinancials.Money(request.Amount) || used + request.Amount > payment.Amount || newCredit > invoice.BalanceDue)
   return BadRequest(new { message = "Allocate an amount within the unused receipt and outstanding invoice balance." });
  db.PaymentAllocations.Add(new PaymentAllocation { PaymentId = paymentId, InvoiceId = invoice.Id, Amount = request.Amount, AllocatedByUserId = scope.UserId });
  invoice.AmountPaid += newCredit; invoice.BalanceDue = Math.Max(0, invoice.Total - invoice.AmountPaid);
  invoice.Status = invoice.BalanceDue == 0 ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
  await db.SaveChangesAsync(token);
  if (transaction is not null) await transaction.CommitAsync(token);
  return Ok(InvoiceResponse(invoice));
 }
 [HttpPost("invoices/{id:guid}/credit-notes")]
 public async Task<ActionResult> Credit(Guid id, CreditNoteRequest request, CancellationToken token)
 {
  await using var transaction = await MaintenanceRules.BeginAsync(db, token);
  var invoice = await db.RentalInvoices.Include(x => x.Booking).FirstOrDefaultAsync(x => x.Id == id, token);
  if (invoice is null) return NotFound();
  var scope = await staffScope.GetAsync(User);
  if (scope is null || !scope.HasBranchAccess(invoice.Booking!.BranchId)) return Forbid();
  if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Voided || string.IsNullOrWhiteSpace(request.Reason) || request.Total <= 0 || request.Total != RentalFinancials.Money(request.Total) || request.Total > invoice.BalanceDue)
   return BadRequest(new { message = "Enter a reason and a credit within the unpaid issued invoice balance. Paid amounts require a controlled refund." });
  var previousCredits = await db.CreditNotes.Where(x => x.InvoiceId == id && x.Status == CreditNoteStatus.Applied).SumAsync(x => (decimal?)x.Total, token) ?? 0;
  var tax = RentalFinancials.Money(request.Total * invoice.TaxAmount / Math.Max(invoice.Total + previousCredits, .01m));
  var item = new CreditNote { CreditNoteNumber = $"CN-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..25], InvoiceId = id, Reason = request.Reason.Trim(), Subtotal = request.Total - tax, TaxAmount = tax, Total = request.Total, IssuedByUserId = scope.UserId, Status = CreditNoteStatus.Applied };
  db.CreditNotes.Add(item); invoice.Total -= request.Total; invoice.BalanceDue = Math.Max(0, invoice.Total - invoice.AmountPaid);
  invoice.Status = invoice.BalanceDue == 0 ? InvoiceStatus.Paid : invoice.AmountPaid > 0 ? InvoiceStatus.PartiallyPaid : InvoiceStatus.Issued;
  await db.SaveChangesAsync(token);
  if (transaction is not null) await transaction.CommitAsync(token);
  return Ok(new { item.Id, item.InvoiceId, item.CreditNoteNumber, item.Reason, item.Subtotal, item.TaxAmount, item.Total, item.Status });
 }
 private static object InvoiceResponse(RentalInvoice invoice) => new { invoice.Id, invoice.InvoiceNumber, invoice.BookingId, invoice.Subtotal, invoice.TaxAmount, invoice.TaxInclusive, invoice.Total, invoice.AmountPaid, invoice.BalanceDue, status = invoice.Status.ToString(), invoice.IssuedAt, lines = InvoiceDocumentLines.Read(invoice) };
 [HttpGet("customers/{customerId:guid}/statement")]public async Task<ActionResult> Statement(Guid customerId,CancellationToken token){var scope=await staffScope.GetAsync(User);if(scope is null)return Forbid();var invoices=await db.RentalInvoices.AsNoTracking().Include(x=>x.Booking).Where(x=>x.Booking!.CustomerId==customerId&&(scope.IsAdministrator||x.Booking.BranchId==scope.BranchId)).OrderBy(x=>x.IssuedAt).ToListAsync(token);return Ok(new{customerId,generatedAt=DateTimeOffset.UtcNow,invoices=invoices.Select(InvoiceResponse),total=invoices.Where(x=>x.Status!=InvoiceStatus.Voided&&x.Status!=InvoiceStatus.Draft).Sum(x=>x.Total),paid=invoices.Where(x=>x.Status!=InvoiceStatus.Voided&&x.Status!=InvoiceStatus.Draft).Sum(x=>x.AmountPaid),outstanding=invoices.Where(x=>x.Status!=InvoiceStatus.Voided&&x.Status!=InvoiceStatus.Draft).Sum(x=>x.BalanceDue)});}
}
public sealed record InvoiceLineRequest(string Description,decimal Quantity,decimal UnitPrice,decimal TaxRate,bool IsTaxable);public sealed record AllocationRequest(Guid InvoiceId,decimal Amount);public sealed record CreditNoteRequest(string Reason,decimal Total);
