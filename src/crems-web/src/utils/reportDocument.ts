export type ReportRow = Record<string, unknown>
export type ReportSection = { title: string; columns: string[]; csvColumns?: string[]; rows: ReportRow[] }
export type StaffReport = { title: string; description: string; coverage: string; sections: ReportSection[] }

export const reportLabel = (key: string) => key.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, c => c.toUpperCase())
export function reportValue(value: unknown): string {
  if (value == null || value === '') return '—'
  if (typeof value === 'boolean') return value ? 'Yes' : 'No'
  if (typeof value === 'number') return value.toLocaleString('en-FJ', { maximumFractionDigits: 2 })
  if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}(T|$)/.test(value) && !Number.isNaN(Date.parse(value))) return new Date(value).toLocaleDateString('en-FJ', { timeZone: 'Pacific/Fiji' })
  return String(value)
}
export function reportSection(title: string, rows: ReportRow[], columns: string[], csvColumns?: string[]): ReportSection { return { title, rows, columns, csvColumns } }
export function filterReport(report: StaffReport, search: string, from: string, to: string): StaffReport {
  const term = search.trim().toLowerCase()
  return { ...report, sections: report.sections.map(section => ({ ...section, rows: section.rows.filter(row => {
    const matches = !term || (section.csvColumns ?? section.columns).some(key => reportValue(row[key]).toLowerCase().includes(term))
    // Each report declares its primary date as the "date" column. Undated sections remain visible.
    const raw = row.date
    const date = typeof raw === 'string' && !Number.isNaN(Date.parse(raw))
      ? (raw.includes('T') ? new Intl.DateTimeFormat('en-CA', { timeZone: 'Pacific/Fiji', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(raw)) : raw.slice(0, 10)) : ''
    return matches && (!date || ((!from || date >= from) && (!to || date <= to)))
  }) })) }
}
function saveBlob(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob); const link = document.createElement('a')
  link.href = url; link.download = name; link.click(); window.setTimeout(() => URL.revokeObjectURL(url), 1000)
}
export function reportCsv(report: StaffReport): string {
  const columns = [...new Set(report.sections.flatMap(section => section.csvColumns ?? section.columns))]
  const generatedAt = `${new Intl.DateTimeFormat('sv-SE', { timeZone: 'Pacific/Fiji', dateStyle: 'short', timeStyle: 'medium' }).format(new Date())} Pacific/Fiji`
  const value = (input: unknown): string => {
    if (input == null) return ''
    if (typeof input === 'number') return Number.isFinite(input) ? String(input) : ''
    if (typeof input === 'boolean') return input ? 'Yes' : 'No'
    if (Array.isArray(input) || typeof input === 'object') return JSON.stringify(input)
    return String(input)
  }
  const cell = (input: unknown) => {
    const raw = value(input)
    // Spreadsheet applications interpret leading =, +, -, @ and control characters as formulas.
    const safe = typeof input === 'string' && /^[\s\u0000-\u001f]*[=+\-@]/.test(raw) ? `'${raw}` : raw
    return `"${safe.replace(/"/g, '""')}"`
  }
  const heading = ['Report', 'Section', 'Generated at (Fiji)', 'Coverage', 'Row number', ...columns.map(reportLabel)]
  const rows = report.sections.flatMap(section => section.rows.map((row, index) => [
    report.title, section.title, generatedAt, report.coverage, index + 1,
    ...columns.map(key => (section.csvColumns ?? section.columns).includes(key) ? row[key] : null),
  ]))
  return [heading, ...rows].map(row => row.map(cell).join(',')).join('\r\n') + '\r\n'
}
export function downloadReportCsv(report: StaffReport) { saveBlob(new Blob(['\uFEFF', reportCsv(report)], { type: 'text/csv;charset=utf-8' }), `${report.title.replace(/[^a-z0-9]+/gi, '-')}.csv`) }
export async function buildReportPdf(report: StaffReport) {
  const { jsPDF } = await import('jspdf')
  const pdf = new jsPDF({ orientation: 'landscape', unit: 'mm', format: 'a4' })
  const margin = 14, width = 269, bottom = 190; let y = 42
  const generated = new Date().toLocaleString('en-FJ', { timeZone: 'Pacific/Fiji' })
  const header = () => {
    pdf.setFillColor(22, 23, 20); pdf.rect(0, 0, 297, 29, 'F'); pdf.setFillColor(255, 221, 0); pdf.rect(0, 29, 297, 1.5, 'F')
    pdf.setTextColor(255, 221, 0); pdf.setFont('helvetica', 'bold'); pdf.setFontSize(10); pdf.text('CARPENTERS RENTALS  /  STAFF REPORT', margin, 10)
    pdf.setTextColor(255); pdf.setFontSize(15); pdf.text(pdf.splitTextToSize(report.title, width)[0], margin, 21); y = 40
  }
  const newPage = () => { pdf.addPage(); header() }
  header()
  pdf.setTextColor(85); pdf.setFont('helvetica', 'normal'); pdf.setFontSize(9)
  for (const line of pdf.splitTextToSize(`${report.description}\n${report.coverage}`, width)) { if (y > bottom) newPage(); pdf.text(line, margin, y); y += 5 }
  y += 5
  for (const section of report.sections) {
    if (y + 24 > bottom) newPage()
    pdf.setFont('helvetica', 'bold'); pdf.setFontSize(11); pdf.setTextColor(25); pdf.text(`${section.title} (${section.rows.length})`, margin, y); y += 6
    const colWidth = width / Math.max(section.columns.length, 1)
    const tableHeader = () => {
      pdf.setFillColor(238, 239, 234); pdf.rect(margin, y, width, 12, 'F'); pdf.setFontSize(8); pdf.setTextColor(45); pdf.setFont('helvetica', 'bold')
      section.columns.forEach((key, i) => pdf.text(pdf.splitTextToSize(reportLabel(key), colWidth - 4).slice(0, 2), margin + i * colWidth + 2, y + 4))
      y += 14
    }
    tableHeader()
    for (const row of section.rows) {
      pdf.setFont('helvetica', 'normal'); pdf.setFontSize(8); pdf.setTextColor(35)
      const cells: string[][] = section.columns.map(key => pdf.splitTextToSize(reportValue(row[key]).replace(/—/g, '-'), colWidth - 4))
      let offset = 0; const count = Math.max(...cells.map(lines => lines.length))
      while (offset < count) {
        if (y + 8 > bottom) { newPage(); tableHeader(); pdf.setFont('helvetica', 'normal'); pdf.setFontSize(8); pdf.setTextColor(35) }
        const take = Math.min(count - offset, Math.max(1, Math.floor((bottom - y - 3) / 4)))
        cells.forEach((lines, i) => { const part = lines.slice(offset, offset + take); if (part.length) pdf.text(part, margin + i * colWidth + 2, y + 3, { lineHeightFactor: 1.4 }) })
        y += take * 4 + 3; offset += take
      }
      pdf.setDrawColor(225); pdf.line(margin, y, margin + width, y); y += 2
    }
    if (!section.rows.length) { pdf.setFont('helvetica', 'normal'); pdf.setTextColor(110); pdf.text('No matching records.', margin + 2, y + 3); y += 8 }
    y += 8
  }
  const pages = pdf.getNumberOfPages()
  for (let page = 1; page <= pages; page++) { pdf.setPage(page); pdf.setFont('helvetica', 'normal'); pdf.setFontSize(8); pdf.setTextColor(110); pdf.text(`Generated ${generated} (Fiji) | FJD where indicated | Internal use`, margin, 202); pdf.text(`${page} / ${pages}`, 283, 202, { align: 'right' }) }
  return pdf
}
export async function downloadStaffReportPdf(report: StaffReport) { (await buildReportPdf(report)).save(`${report.title.replace(/[^a-z0-9]+/gi, '-')}.pdf`) }
