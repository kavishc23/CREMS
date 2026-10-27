import { downloadRentalFinancialPdf } from './rentalFinancialPdf'
type InvoiceDocument = {
  invoiceNumber: string; bookingNumber: string; customerName: string; branchName: string
  total: number; amountPaid: number; balanceDue: number; status: string; issuedAt: string
}
type ReceiptDocument = {
  receiptNumber: string; bookingNumber: string; customerName: string; branchName: string
  type: string; method: string; amount: number; status: string; createdAt: string
}

const money = (value: number) => `FJD ${value.toLocaleString('en-FJ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
const date = (value: string) => new Date(value).toLocaleString('en-FJ', { day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' })

async function documentShell(title: string, reference: string, lines: [string, string][], filename: string) {
  const { jsPDF } = await import('jspdf')
  const pdf = new jsPDF({ unit: 'mm', format: 'a4' })
  const margin = 18; const width = 174; let y = 22
  const page = () => {
    pdf.setFillColor(20, 20, 20); pdf.rect(0, 0, 210, 34, 'F')
    pdf.setTextColor(255, 220, 0); pdf.setFont('helvetica', 'bold'); pdf.setFontSize(16); pdf.text('CARPENTERS RENTALS', margin, 16)
    pdf.setTextColor(255); pdf.setFontSize(9); pdf.setFont('helvetica', 'normal'); pdf.text(title.toUpperCase(), margin, 24)
    y = 46
  }
  const write = (label: string, value: string) => {
    const wrapped = pdf.splitTextToSize(value || '-', 108)
    const height = Math.max(10, wrapped.length * 5 + 4)
    if (y + height > 274) { pdf.addPage(); page() }
    pdf.setTextColor(100); pdf.setFont('helvetica', 'bold'); pdf.setFontSize(8); pdf.text(label.toUpperCase(), margin, y)
    pdf.setTextColor(25); pdf.setFont('helvetica', 'normal'); pdf.setFontSize(10); pdf.text(wrapped, margin + 52, y)
    pdf.setDrawColor(225); pdf.line(margin, y + height - 2, margin + width, y + height - 2); y += height
  }
  page(); pdf.setTextColor(25); pdf.setFont('helvetica', 'bold'); pdf.setFontSize(16); pdf.text(reference, margin, y); y += 14
  lines.forEach(([label, value]) => write(label, value))
  pdf.setFontSize(7.5); pdf.setTextColor(105); pdf.text(`Generated ${date(new Date().toISOString())} | CREMS staff record`, margin, 288)
  pdf.save(filename)
}

export function downloadInvoicePdf(invoice: InvoiceDocument) {
  return downloadRentalFinancialPdf({ kind: 'Invoice', reference: invoice.invoiceNumber,
    details: [['Booking', invoice.bookingNumber], ['Customer', invoice.customerName], ['Branch', invoice.branchName], ['Issued', date(invoice.issuedAt)], ['Status', invoice.status]],
    lines: [{ description: 'Recorded rental invoice total (including VAT)', quantity: 1, rate: invoice.total }],
    totals: [{ label: 'Invoice total', amount: invoice.total, emphasis: true }, { label: 'Payments received', amount: invoice.amountPaid }, { label: 'Balance due', amount: invoice.balanceDue, emphasis: true }],
    note: 'Summary of the recorded invoice. Contact the issuing branch for the full itemised account and payment arrangements.' })
}

export function downloadReceiptPdf(receipt: ReceiptDocument) {
  return documentShell('Payment receipt', receipt.receiptNumber, [
    ['Booking', receipt.bookingNumber], ['Customer', receipt.customerName], ['Branch', receipt.branchName], ['Recorded', date(receipt.createdAt)],
    ['Payment type', receipt.type.replace(/([a-z])([A-Z])/g, '$1 $2')], ['Method', receipt.method.replace(/([a-z])([A-Z])/g, '$1 $2')], ['Amount received', money(receipt.amount)], ['Status', receipt.status],
  ], `${receipt.receiptNumber}.pdf`)
}

export function downloadCustomerStatementPdf(customerName: string, invoices: InvoiceDocument[]) {
  const total = invoices.reduce((sum, item) => sum + item.total, 0)
  const paid = invoices.reduce((sum, item) => sum + item.amountPaid, 0)
  const outstanding = invoices.reduce((sum, item) => sum + item.balanceDue, 0)
  return documentShell('Customer statement', customerName, [
    ['Statement date', date(new Date().toISOString())], ['Invoices included', String(invoices.length)], ['Total invoiced', money(total)], ['Payments received', money(paid)], ['Balance outstanding', money(outstanding)],
    ...invoices.map(item => [`${item.invoiceNumber} · ${new Date(item.issuedAt).toLocaleDateString('en-FJ')}`, `${item.bookingNumber} | Total ${money(item.total)} | Balance ${money(item.balanceDue)}`] as [string, string]),
  ], `statement-${customerName.replace(/[^a-z0-9]+/gi, '-').toLowerCase()}.pdf`)
}

export function downloadReturnSettlementPdf(data: { bookingNumber: string; customerName: string; branchName: string; assetName: string | null; bondHeld: number; deduction: number; refund: number; charges: number; reason: string | null; acknowledgedBy: string }) {
  return documentShell('Return and bond settlement', data.bookingNumber, [
    ['Customer', data.customerName], ['Branch', data.branchName], ['Asset', data.assetName || 'Not recorded'], ['Additional charges before VAT', money(data.charges)],
    ['Refundable bond held', money(data.bondHeld)], ['Bond deduction', money(data.deduction)], ['Bond refund due', money(data.refund)], ['Deduction reason', data.reason || 'No deduction recorded'], ['Customer acknowledgement', data.acknowledgedBy],
  ], `${data.bookingNumber}-return-settlement.pdf`)
}

export function downloadManagementReportPdf(data: { quoteTotal: number; quoteConverted: number; quoteRate: number; downtimeHours: number; damageIncidents: number; damageEstimatedCost: number; damageUnresolved: number; topCustomers: { name: string | null; customerNumber: string | null; rentals: number; completed: number; revenue: number }[] }) {
  return documentShell('Management report', 'Lifetime operational management snapshot', [
    ['Generated', date(new Date().toISOString())], ['Quotes', `${data.quoteConverted} converted from ${data.quoteTotal} (${data.quoteRate}%)`], ['Asset downtime', `${data.downtimeHours.toLocaleString('en-FJ')} hours`],
    ['Damage recovery', `${data.damageIncidents} incident(s); estimated ${money(data.damageEstimatedCost)}; ${data.damageUnresolved} unresolved`],
    ...data.topCustomers.slice(0, 10).map(customer => [`${customer.customerNumber || 'Customer'} · ${customer.name || 'Unknown'}`, `${customer.rentals} rentals | ${customer.completed} completed | Revenue ${money(customer.revenue)}`] as [string, string]),
  ], `management-report-${new Date().toISOString().slice(0, 10)}.pdf`)
}
