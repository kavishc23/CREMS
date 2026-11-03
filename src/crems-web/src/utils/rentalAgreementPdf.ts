export type AgreementFinancialLine = { description: string; quantity: number; rate: number; unit: string; isTaxable: boolean; amount: number }
export type AgreementTerm = { title: string; content: string }
export type RentalAgreementData = {
  approved: boolean; agreementNumber: string | null; termsVersion: string
  customer: { customerNumber: string; name: string; type: string; email: string | null; phone: string | null; address: string | null; identificationNumber: string | null }
  asset: { assetNumber: string; name: string; type: string; registrationNumber: string | null; serialNumber: string | null }
  rental: { bookingNumber: string; branchName: string; branchAddress: string | null; branchPhone: string | null; startAt: string; endAt: string; days: number }
  settlement?: { stage: string; invoiceNumber: string | null; lines: AgreementFinancialLine[]; subtotal: number; taxInclusive?: boolean; adjustment?: number; taxAmount: number; total: number; paid: number; bondAppliedToInvoice?: number; balanceDue: number; bondAmountHeld: number; bondDeductionAmount: number; bondRefundAmount: number; bondDeductionReason: string | null; payments: { receiptNumber: string; type: string; method: string; amount: number; createdAt: string }[] }
  pricing: { lines?: AgreementFinancialLine[]; hireUnit?:string; hireQuantity?:number; dailyRate: number; subtotal: number; discountAmount: number; additionalCharges: number; additionalChargesDescription: string | null; taxRate: number; taxAmount: number; total: number; depositRequired: number }
  terms: AgreementTerm[]; customerSignatureName: string | null; customerSignatureDataUrl: string | null; customerSignedAt: string | null; approvedByName: string | null; approvedAt: string | null; lastEmailedTo?: string | null; lastEmailedAt?: string | null
}

async function loadLogo() {
  const blob = await (await fetch('/brand/carpenters-logo.png')).blob()
  return await new Promise<string>((resolve, reject) => { const reader = new FileReader(); reader.onload = () => resolve(String(reader.result)); reader.onerror = reject; reader.readAsDataURL(blob) })
}

export async function downloadRentalAgreementPdf(data: RentalAgreementData) {
  const [{ jsPDF }, logo] = await Promise.all([import('jspdf'), loadLogo()])
  const doc = createRentalAgreementPdf(data, logo, jsPDF)
  doc.save(`${data.agreementNumber}.pdf`)
}

export async function openRentalAgreementPdf(data: RentalAgreementData) {
  const [{ jsPDF }, logo] = await Promise.all([import('jspdf'), loadLogo()])
  const doc = createRentalAgreementPdf(data, logo, jsPDF)
  const url = String(doc.output('bloburl'))
  window.open(url, '_blank', 'noopener,noreferrer')
  window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
}

function createRentalAgreementPdf(data: RentalAgreementData, logo: string, Pdf: typeof import('jspdf').jsPDF) {
  if (!data.approved || !data.agreementNumber) throw new Error('Only approved agreements can be exported.')
  const doc = new Pdf({ unit: 'mm', format: 'a4' }); const pageWidth = 210; const margin = 16; const contentWidth = pageWidth - margin * 2
  let y = 18
  const addHeader = () => { doc.addImage(logo, 'PNG', margin, 10, 18, 18); doc.setTextColor(20); doc.setFont('helvetica', 'bold'); doc.setFontSize(15); doc.text('CARPENTERS RENTALS', 38, 17); doc.setFontSize(9); doc.setFont('helvetica', 'normal'); doc.text('Vehicle & Equipment Rental Agreement', 38, 23); doc.setDrawColor(255, 220, 0); doc.setLineWidth(1.2); doc.line(margin, 31, pageWidth - margin, 31); y = 38 }
  const ensure = (height: number) => { if (y + height > 278) { doc.addPage(); addHeader() } }
  const labelValue = (label: string, value: string, x: number, width: number) => { doc.setFontSize(7.5); doc.setTextColor(105); doc.setFont('helvetica', 'bold'); doc.text(label.toUpperCase(), x, y); doc.setFontSize(9.5); doc.setTextColor(20); doc.setFont('helvetica', 'normal'); const lines = doc.splitTextToSize(value || '-', width); doc.text(lines, x, y + 5); return Math.max(10, lines.length * 4.5 + 5) }
  const sectionTitle = (title: string) => { ensure(14); doc.setFillColor(20, 20, 20); doc.rect(margin, y, contentWidth, 8, 'F'); doc.setTextColor(255, 237, 0); doc.setFont('helvetica', 'bold'); doc.setFontSize(10); doc.text(title, margin + 3, y + 5.5); y += 13 }

  addHeader(); doc.setFillColor(247, 247, 243); doc.roundedRect(margin, y, contentWidth, 24, 2, 2, 'F'); doc.setTextColor(20); doc.setFont('helvetica', 'bold'); doc.setFontSize(11); doc.text(`Agreement: ${data.agreementNumber}`, margin + 4, y + 7); doc.setFont('helvetica', 'normal'); doc.setFontSize(9); doc.text(`Booking: ${data.rental.bookingNumber}`, margin + 4, y + 14); doc.text(`Terms version: ${data.termsVersion}`, margin + 80, y + 14); doc.text(`Branch: ${data.rental.branchName}`, margin + 4, y + 20); y += 31

  sectionTitle('Customer and rental details'); let rowY = y; const a = labelValue('Customer', `${data.customer.name} (${data.customer.customerNumber})`, margin, 82); y = rowY; const b = labelValue('Customer type', data.customer.type, margin + 94, 82); y = rowY + Math.max(a, b); rowY = y; const c = labelValue('Contact', `${data.customer.email || '-'} | ${data.customer.phone || '-'}`, margin, 82); y = rowY; const d = labelValue('Identification', data.customer.identificationNumber || '-', margin + 94, 82); y = rowY + Math.max(c, d); rowY = y; const e = labelValue('Address', data.customer.address || '-', margin, 82); y = rowY; const f = labelValue('Rental period', `${new Date(data.rental.startAt).toLocaleDateString('en-FJ')} to ${new Date(data.rental.endAt).toLocaleDateString('en-FJ')} (${data.rental.days} day${data.rental.days === 1 ? '' : 's'})`, margin + 94, 82); y = rowY + Math.max(e, f) + 2

  sectionTitle('Rental asset'); rowY = y; const g = labelValue('Asset', `${data.asset.assetNumber} - ${data.asset.name}`, margin, 82); y = rowY; const h = labelValue('Type', data.asset.type, margin + 94, 82); y = rowY + Math.max(g, h); rowY = y; const i = labelValue('Registration', data.asset.registrationNumber || '-', margin, 82); y = rowY; const j = labelValue('Serial number', data.asset.serialNumber || '-', margin + 94, 82); y = rowY + Math.max(i, j) + 2

  const money = (value: number) => `FJD ${Number(value).toFixed(2)}`
  const financialRow = (label: string, amount: number, bold = false) => {
    doc.setFont('helvetica', bold ? 'bold' : 'normal'); doc.setFontSize(9); doc.setTextColor(30)
    const lines = doc.splitTextToSize(label, contentWidth - 53) as string[]
    ensure(Math.max(7, lines.length * 4.5 + 3))
    doc.setFont('helvetica', bold ? 'bold' : 'normal'); doc.setFontSize(9); doc.setTextColor(30)
    doc.text(lines, margin + 3, y); doc.text(money(amount), pageWidth - margin - 3, y, { align: 'right' }); y += Math.max(7, lines.length * 4.5 + 3)
  }
  sectionTitle('Signed pickup pricing schedule (FJD)')
  if (data.pricing.lines?.length) {
    data.pricing.lines.forEach(line => financialRow(`${line.description} | ${line.quantity} ${line.unit} x ${money(line.rate)}${line.isTaxable ? '' : ' | VAT exempt'}`, line.amount))
  } else {
    financialRow('Base hire', data.pricing.subtotal)
    financialRow(data.pricing.additionalChargesDescription || 'Agreed additional services', data.pricing.additionalCharges)
    financialRow('Discount', -data.pricing.discountAmount)
  }
  financialRow('Net rental revenue before VAT', Math.max(0, data.pricing.subtotal - data.pricing.discountAmount + data.pricing.additionalCharges), true)
  financialRow(`VAT / tax (${data.pricing.taxRate}%)`, data.pricing.taxAmount)
  financialRow('Rental charge including VAT', data.pricing.total, true)
  financialRow('Refundable bond required (separate from revenue)', data.pricing.depositRequired)
  y += 4
  if (data.settlement) {
    const settlement = data.settlement
    sectionTitle(`${settlement.stage} (FJD)`)
    if (settlement.invoiceNumber) { ensure(8); doc.setTextColor(30); doc.setFontSize(9); doc.text(`Invoice: ${settlement.invoiceNumber}`, margin + 3, y); y += 8 }
    settlement.lines.forEach(line => financialRow(`${line.description} | ${line.quantity} ${line.unit} x ${money(line.rate)}${line.isTaxable ? '' : ' | VAT exempt'}`, line.amount))
    financialRow(settlement.taxInclusive ? 'Rental charges including VAT' : 'Net rental and service charges before VAT', settlement.subtotal, true)
    financialRow(settlement.taxInclusive ? 'VAT included above' : 'VAT / tax', settlement.taxAmount)
    if(settlement.adjustment) financialRow('Invoice adjustments / credits', settlement.adjustment)
    financialRow('Rental charge including VAT', settlement.total, true)
    financialRow('Hire payments received', settlement.paid - (settlement.bondAppliedToInvoice ?? 0))
    financialRow('Bond applied to invoice', settlement.bondAppliedToInvoice ?? 0)
    financialRow('Hire balance due', settlement.balanceDue, true)
    financialRow('Refundable bond collected', settlement.bondAmountHeld)
    financialRow('Bond deduction', settlement.bondDeductionAmount)
    financialRow('Bond refunded', settlement.bondRefundAmount)
    if (settlement.bondDeductionReason) {
      const notes = doc.splitTextToSize(`Bond deduction reason: ${settlement.bondDeductionReason}`, contentWidth - 6) as string[]
      notes.forEach(line => { ensure(6); doc.setFontSize(9); doc.setTextColor(30); doc.text(line, margin + 3, y); y += 5 })
    }
    settlement.payments.forEach(payment => financialRow(`${payment.receiptNumber} | ${payment.type} | ${payment.method}`, payment.amount))
    y += 4
  }

  sectionTitle('Terms and conditions'); data.terms.forEach((term) => { const lines = doc.splitTextToSize(term.content, contentWidth - 5) as string[]; ensure(14); doc.setTextColor(20); doc.setFont('helvetica', 'bold'); doc.setFontSize(9.2); doc.text(term.title, margin + 2, y); y += 5; doc.setFont('helvetica', 'normal'); doc.setFontSize(8.5); doc.setTextColor(55); for(const line of lines){ensure(5);doc.setFont('helvetica','normal');doc.setFontSize(8.5);doc.setTextColor(55);doc.text(line,margin+2,y);y+=4.2} y += 4 })

  sectionTitle('Signatures and approval'); ensure(54); doc.setTextColor(20); doc.setFontSize(9); doc.setFont('helvetica', 'normal'); doc.text('By signing below, the customer accepts this agreement and acknowledges receiving a copy.', margin + 2, y); y += 8; if (data.customerSignatureDataUrl) { try { doc.addImage(data.customerSignatureDataUrl, 'PNG', margin + 2, y, 72, 20) } catch { /* retain typed signature if an old image cannot be decoded */ } } doc.setDrawColor(80); doc.line(margin + 2, y + 23, margin + 80, y + 23); doc.line(margin + 98, y + 23, pageWidth - margin - 2, y + 23); doc.setFont('helvetica', 'bold'); doc.text(data.customerSignatureName || '', margin + 2, y + 28); doc.text(data.approvedByName || '', margin + 98, y + 18); doc.setFont('helvetica', 'normal'); doc.setFontSize(7.5); doc.setTextColor(90); doc.text(`Customer signature | ${data.customerSignedAt ? new Date(data.customerSignedAt).toLocaleString('en-FJ') : ''}`, margin + 2, y + 34); doc.text(`Approved by rental agent | ${data.approvedAt ? new Date(data.approvedAt).toLocaleString('en-FJ') : ''}`, margin + 98, y + 28)

  const pages = doc.getNumberOfPages(); for (let page = 1; page <= pages; page++) { doc.setPage(page); doc.setDrawColor(220); doc.line(margin, 287, pageWidth - margin, 287); doc.setFontSize(7); doc.setTextColor(110); doc.text('Carpenters Rentals | Operational template - subject to approved company terms and applicable Fiji law', margin, 292); doc.text(`Page ${page} of ${pages}`, pageWidth - margin, 292, { align: 'right' }) }
  return doc
}
