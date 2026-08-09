import { jsPDF } from 'jspdf'

export type AgreementTerm = { title: string; content: string }
export type RentalAgreementData = {
  approved: boolean; agreementNumber: string | null; termsVersion: string
  customer: { customerNumber: string; name: string; type: string; email: string | null; phone: string | null; address: string | null; identificationNumber: string | null }
  asset: { assetNumber: string; name: string; type: string; registrationNumber: string | null; serialNumber: string | null }
  rental: { bookingNumber: string; branchName: string; branchAddress: string | null; branchPhone: string | null; startAt: string; endAt: string; days: number }
  pricing: { dailyRate: number; subtotal: number; discountAmount: number; additionalCharges: number; additionalChargesDescription: string | null; taxRate: number; taxAmount: number; total: number; depositRequired: number }
  terms: AgreementTerm[]; customerSignatureName: string | null; customerSignatureDataUrl: string | null; customerSignedAt: string | null; approvedByName: string | null; approvedAt: string | null
}

async function loadLogo() {
  const blob = await (await fetch('/brand/carpenters-logo.png')).blob()
  return await new Promise<string>((resolve, reject) => { const reader = new FileReader(); reader.onload = () => resolve(String(reader.result)); reader.onerror = reject; reader.readAsDataURL(blob) })
}

export async function downloadRentalAgreementPdf(data: RentalAgreementData) {
  const logo = await loadLogo()
  const doc = createRentalAgreementPdf(data, logo)
  doc.save(`${data.agreementNumber}.pdf`)
}

export function createRentalAgreementPdf(data: RentalAgreementData, logo: string) {
  if (!data.approved || !data.agreementNumber) throw new Error('Only approved agreements can be exported.')
  const doc = new jsPDF({ unit: 'mm', format: 'a4' }); const pageWidth = 210; const margin = 16; const contentWidth = pageWidth - margin * 2
  let y = 18
  const addHeader = () => { doc.addImage(logo, 'PNG', margin, 10, 18, 18); doc.setTextColor(20); doc.setFont('helvetica', 'bold'); doc.setFontSize(15); doc.text('CARPENTERS RENTALS', 38, 17); doc.setFontSize(9); doc.setFont('helvetica', 'normal'); doc.text('Vehicle & Equipment Rental Agreement', 38, 23); doc.setDrawColor(255, 220, 0); doc.setLineWidth(1.2); doc.line(margin, 31, pageWidth - margin, 31); y = 38 }
  const ensure = (height: number) => { if (y + height > 278) { doc.addPage(); addHeader() } }
  const labelValue = (label: string, value: string, x: number, width: number) => { doc.setFontSize(7.5); doc.setTextColor(105); doc.setFont('helvetica', 'bold'); doc.text(label.toUpperCase(), x, y); doc.setFontSize(9.5); doc.setTextColor(20); doc.setFont('helvetica', 'normal'); const lines = doc.splitTextToSize(value || '-', width); doc.text(lines, x, y + 5); return Math.max(10, lines.length * 4.5 + 5) }
  const sectionTitle = (title: string) => { ensure(14); doc.setFillColor(20, 20, 20); doc.rect(margin, y, contentWidth, 8, 'F'); doc.setTextColor(255, 237, 0); doc.setFont('helvetica', 'bold'); doc.setFontSize(10); doc.text(title, margin + 3, y + 5.5); y += 13 }

  addHeader(); doc.setFillColor(247, 247, 243); doc.roundedRect(margin, y, contentWidth, 24, 2, 2, 'F'); doc.setTextColor(20); doc.setFont('helvetica', 'bold'); doc.setFontSize(11); doc.text(`Agreement: ${data.agreementNumber}`, margin + 4, y + 7); doc.setFont('helvetica', 'normal'); doc.setFontSize(9); doc.text(`Booking: ${data.rental.bookingNumber}`, margin + 4, y + 14); doc.text(`Terms version: ${data.termsVersion}`, margin + 80, y + 14); doc.text(`Branch: ${data.rental.branchName}`, margin + 4, y + 20); y += 31

  sectionTitle('Customer and rental details'); let rowY = y; const a = labelValue('Customer', `${data.customer.name} (${data.customer.customerNumber})`, margin, 82); y = rowY; const b = labelValue('Customer type', data.customer.type, margin + 94, 82); y = rowY + Math.max(a, b); rowY = y; const c = labelValue('Contact', `${data.customer.email || '-'} | ${data.customer.phone || '-'}`, margin, 82); y = rowY; const d = labelValue('Identification', data.customer.identificationNumber || '-', margin + 94, 82); y = rowY + Math.max(c, d); rowY = y; const e = labelValue('Address', data.customer.address || '-', margin, 82); y = rowY; const f = labelValue('Rental period', `${new Date(data.rental.startAt).toLocaleDateString('en-FJ')} to ${new Date(data.rental.endAt).toLocaleDateString('en-FJ')} (${data.rental.days} day${data.rental.days === 1 ? '' : 's'})`, margin + 94, 82); y = rowY + Math.max(e, f) + 2

  sectionTitle('Rental asset'); rowY = y; const g = labelValue('Asset', `${data.asset.assetNumber} - ${data.asset.name}`, margin, 82); y = rowY; const h = labelValue('Type', data.asset.type, margin + 94, 82); y = rowY + Math.max(g, h); rowY = y; const i = labelValue('Registration', data.asset.registrationNumber || '-', margin, 82); y = rowY; const j = labelValue('Serial number', data.asset.serialNumber || '-', margin + 94, 82); y = rowY + Math.max(i, j) + 2

  sectionTitle('Pricing summary (FJD)'); const money = (value: number) => `$${Number(value).toFixed(2)}`; const pricing = [['Daily rate', money(data.pricing.dailyRate)], ['Rental subtotal', money(data.pricing.subtotal)], ['Discount', `-${money(data.pricing.discountAmount)}`], ['Additional charges', money(data.pricing.additionalCharges)], [`VAT / tax (${data.pricing.taxRate}%)`, money(data.pricing.taxAmount)], ['Estimated total', money(data.pricing.total)], ['Security deposit', money(data.pricing.depositRequired)]]; pricing.forEach(([label, value], index) => { ensure(9); if (index === 5) { doc.setDrawColor(30); doc.line(margin, y - 2, pageWidth - margin, y - 2); y += 3; doc.setFont('helvetica', 'bold') } else doc.setFont('helvetica', 'normal'); doc.setFontSize(9); doc.setTextColor(30); doc.text(label, margin + 3, y); doc.text(value, pageWidth - margin - 3, y, { align: 'right' }); y += 6 }); y += 4

  sectionTitle('Terms and conditions'); data.terms.forEach((term) => { const lines = doc.splitTextToSize(term.content, contentWidth - 5); ensure(9 + lines.length * 4); doc.setTextColor(20); doc.setFont('helvetica', 'bold'); doc.setFontSize(9.2); doc.text(term.title, margin + 2, y); y += 5; doc.setFont('helvetica', 'normal'); doc.setFontSize(8.5); doc.setTextColor(55); doc.text(lines, margin + 2, y, { lineHeightFactor: 1.25 }); y += lines.length * 4.2 + 4 })

  sectionTitle('Signatures and approval'); ensure(54); doc.setTextColor(20); doc.setFontSize(9); doc.setFont('helvetica', 'normal'); doc.text('By signing below, the customer accepts this agreement and acknowledges receiving a copy.', margin + 2, y); y += 8; if (data.customerSignatureDataUrl) { try { doc.addImage(data.customerSignatureDataUrl, 'PNG', margin + 2, y, 72, 20) } catch { /* retain typed signature if an old image cannot be decoded */ } } doc.setDrawColor(80); doc.line(margin + 2, y + 23, margin + 80, y + 23); doc.line(margin + 98, y + 23, pageWidth - margin - 2, y + 23); doc.setFont('helvetica', 'bold'); doc.text(data.customerSignatureName || '', margin + 2, y + 28); doc.text(data.approvedByName || '', margin + 98, y + 18); doc.setFont('helvetica', 'normal'); doc.setFontSize(7.5); doc.setTextColor(90); doc.text(`Customer signature | ${data.customerSignedAt ? new Date(data.customerSignedAt).toLocaleString('en-FJ') : ''}`, margin + 2, y + 34); doc.text(`Approved by rental agent | ${data.approvedAt ? new Date(data.approvedAt).toLocaleString('en-FJ') : ''}`, margin + 98, y + 28)

  const pages = doc.getNumberOfPages(); for (let page = 1; page <= pages; page++) { doc.setPage(page); doc.setDrawColor(220); doc.line(margin, 287, pageWidth - margin, 287); doc.setFontSize(7); doc.setTextColor(110); doc.text('Carpenters Rentals | Operational template - subject to approved company terms and applicable Fiji law', margin, 292); doc.text(`Page ${page} of ${pages}`, pageWidth - margin, 292, { align: 'right' }) }
  return doc
}
