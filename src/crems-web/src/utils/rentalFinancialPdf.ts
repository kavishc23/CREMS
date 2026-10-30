export type RentalFinancialDocument = {
  kind: 'Invoice' | 'Quotation' | 'Receipt'
  reference: string
  details: [string, string][]
  lines: { description: string; quantity: number; rate: number; unit?: string; amount?: number }[]
  totals: { label: string; amount: number; emphasis?: boolean }[]
  note?: string
}

const money = (amount: number) => `FJD ${amount.toLocaleString('en-FJ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
const printable = (text: string) => text.replace(/[—–]/g, '-').replace(/×/g, 'x').replace(/…/g, '...')

export async function buildRentalFinancialPdf(data: RentalFinancialDocument) {
  const { jsPDF } = await import('jspdf')
  const pdf = new jsPDF({ unit: 'mm', format: 'a4' })
  let logo: string | null = null
  try {
    const response = await fetch('/brand/carpenters-logo.png')
    if (response.ok) {
      const blob = await response.blob()
      logo = await new Promise<string>((resolve, reject) => { const reader = new FileReader(); reader.onload = () => resolve(String(reader.result)); reader.onerror = reject; reader.readAsDataURL(blob) })
    }
  } catch { /* The branded text header remains available when offline. */ }
  const left = 16, right = 194, bottom = 275
  let y = 0
  function header() {
    pdf.setFillColor(20, 21, 18); pdf.rect(0, 0, 210, 34, 'F')
    if (logo) { try { pdf.addImage(logo, 'PNG', left, 8, 18, 18) } catch { /* Preserve the text header if the image cannot be decoded. */ } }
    const x = logo ? 40 : left
    pdf.setFont('helvetica', 'bold'); pdf.setFontSize(13); pdf.setTextColor(255)
    pdf.text('CARPENTERS RENTALS & HIRE', x, 15)
    pdf.setFont('helvetica', 'normal'); pdf.setFontSize(9); pdf.setTextColor(255, 237, 0)
    pdf.text(`CREMS  /  RENTAL ${data.kind.toUpperCase()}`, x, 23)
    pdf.setFillColor(255, 237, 0); pdf.rect(0, 34, 210, 1.5, 'F'); y = 45
  }
  function nextPage() { pdf.addPage(); header() }
  function ensure(height: number) { if (y + height > bottom) nextPage() }
  function textLines(text: string, width: number, size = 9): string[] {
    pdf.setFontSize(size)
    return pdf.splitTextToSize(printable(text), width) as string[]
  }
  function paragraph(text: string, width = right - left) {
    pdf.setFont('helvetica', 'normal'); pdf.setTextColor(80)
    for (const line of textLines(text, width)) { ensure(5); pdf.text(line, left, y); y += 4.5 }
  }
  header()
  pdf.setFont('helvetica', 'bold'); pdf.setFontSize(19); pdf.setTextColor(20)
  pdf.text(`Rental ${data.kind.toLowerCase()}`, left, y); y += 9
  pdf.setFontSize(11)
  for (const line of textLines(data.reference, 178, 11)) { ensure(6); pdf.text(line, left, y); y += 5 }
  y += 7
  // Each value wraps within its column, including legacy references and long names.
  for (let index = 0; index < data.details.length; index += 2) {
    const pair = data.details.slice(index, index + 2)
    const values = pair.map(([, value]) => textLines(value || '-', 83))
    const height = Math.max(...values.map(lines => lines.length)) * 4.5 + 12
    ensure(height)
    pair.forEach(([label], column) => {
      const x = left + column * 92
      pdf.setTextColor(105); pdf.setFont('helvetica', 'bold'); pdf.setFontSize(7.5); pdf.text(label.toUpperCase(), x, y)
      pdf.setTextColor(30); pdf.setFont('helvetica', 'normal'); pdf.setFontSize(9); pdf.text(values[column], x, y + 5, { lineHeightFactor: 1.4 })
    })
    y += height
  }
  function tableHeader() {
    ensure(12); pdf.setFillColor(239, 240, 234); pdf.rect(left, y, 178, 9, 'F')
    pdf.setFont('helvetica', 'bold'); pdf.setTextColor(45); pdf.setFontSize(8)
    pdf.text('DESCRIPTION', left + 3, y + 6); pdf.text('QTY', 130, y + 6, { align: 'right' }); pdf.text('RATE (FJD)', 160, y + 6, { align: 'right' }); pdf.text('AMOUNT (FJD)', right - 3, y + 6, { align: 'right' }); y += 13
  }
  tableHeader()
  for (const item of data.lines) {
    const description = textLines(`${item.description}${item.unit ? ` (${item.unit})` : ''}`, 89)
    let offset = 0
    do {
      if (y + 9 > bottom) { nextPage(); tableHeader() }
      const take = Math.min(description.length - offset, Math.max(1, Math.floor((bottom - y - 5) / 4.5)))
      pdf.setFont('helvetica', 'normal'); pdf.setFontSize(9); pdf.setTextColor(30)
      pdf.text(description.slice(offset, offset + take), left + 3, y + 3, { lineHeightFactor: 1.4 })
      if (offset === 0) {
        pdf.text(item.quantity.toLocaleString('en-FJ'), 130, y + 3, { align: 'right' })
        pdf.text(item.rate.toFixed(2), 160, y + 3, { align: 'right' })
        pdf.text((item.amount ?? Math.round((item.quantity * item.rate + Number.EPSILON) * 100) / 100).toFixed(2), right - 3, y + 3, { align: 'right' })
      }
      y += take * 4.5 + 5; offset += take
    } while (offset < description.length)
    pdf.setDrawColor(226); pdf.line(left, y - 2, right, y - 2)
  }
  y += 6
  for (const total of data.totals) {
    ensure(12)
    if (total.emphasis) { pdf.setFillColor(255, 250, 206); pdf.rect(left, y - 4, 178, 11, 'F') }
    pdf.setTextColor(25); pdf.setFont('helvetica', total.emphasis ? 'bold' : 'normal'); pdf.setFontSize(total.emphasis ? 10.5 : 9)
    pdf.text(total.label, left + 3, y + 2); pdf.text(money(total.amount), right - 3, y + 2, { align: 'right' }); y += 12
  }
  if (data.note) { y += 5; paragraph(data.note) }
  const pages = pdf.getNumberOfPages()
  const generated = new Date().toLocaleDateString('en-FJ', { timeZone: 'Pacific/Fiji' })
  for (let page = 1; page <= pages; page++) {
    pdf.setPage(page); pdf.setDrawColor(220); pdf.line(left, 283, right, 283)
    pdf.setFont('helvetica', 'normal'); pdf.setFontSize(7); pdf.setTextColor(105)
    pdf.text(`CREMS | Carpenters Rentals & Hire | Generated ${generated}`, left, 289)
    pdf.text(`Page ${page} of ${pages}`, right, 289, { align: 'right' })
  }
  return pdf
}

export async function downloadRentalFinancialPdf(data: RentalFinancialDocument) {
  ;(await buildRentalFinancialPdf(data)).save(`${data.reference}.pdf`)
}
