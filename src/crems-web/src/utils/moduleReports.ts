import { api } from '../api/client'
import { reportSection as section, type ReportRow, type StaffReport } from './reportDocument'

export function operationsReports(workspace: Record<string, ReportRow[]>, overview?: Record<string, number> | null): StaffReport[] {
  const definitions: [string, string, string[]][] = [
    ['dispatches', 'Deliveries and collections', ['dispatchNumber', 'date', 'type', 'status', 'address', 'assignedDriver']],
    ['tasks', 'Outstanding work', ['title', 'date', 'priority', 'description', 'isCompleted']],
    ['quotes', 'Quotation register', ['quoteNumber', 'date', 'status', 'validUntil', 'total']],
    ['approvals', 'Approval register', ['requestNumber', 'date', 'type', 'status', 'amount', 'reason']],
    ['cases', 'Customer cases', ['caseNumber', 'date', 'type', 'status', 'subject', 'priority']],
    ['assignments', 'Personnel assignments', ['personnelName', 'date', 'endAt', 'customerHourlyRate']],
    ['alerts', 'Business alerts', ['title', 'date', 'category', 'priority', 'message']],
    ['parts', 'Parts stock', ['partNumber', 'name', 'quantityOnHand', 'quantityAllocated', 'reorderLevel']],
    ['purchaseOrders', 'Purchase order register', ['purchaseOrderNumber', 'date', 'supplier', 'status', 'total']],
    ['transfers', 'Asset transfers', ['transferNumber', 'date', 'status', 'reason']],
  ]
  const reports = definitions.filter(([key]) => workspace[key] !== undefined).map(([key, title, columns]) => ({ title, description: 'Operational records for your permitted branches and divisions. Monetary amounts are in FJD.', coverage: 'Current operations workspace snapshot. Transaction lists contain up to 100 records; personnel assignments contain up to 150. This is a register, not a signed transaction document.', sections: [section(title, (workspace[key] ?? []).map(row => ({ ...row, date: row.scheduledAt ?? row.dueAt ?? row.startAt ?? row.raisedAt ?? row.createdAt })), columns)] }))
  if (overview) reports.unshift({ title: 'Operations briefing', description: 'Current workload, overdue hires and operational exceptions.', coverage: 'Current scoped operations overview. Values reflect the time this module was loaded.', sections: [section('Operational summary', ['activeRentals', 'overdueRentals', 'todayDispatches', 'pendingApprovals', 'pendingQuotes', 'openCases', 'activeTasks', 'lowStockParts', 'assetCount', 'rentedAssets', 'utilizationPercent', 'outstandingReceivables'].map(metric => ({ metric: metric.replace(/([a-z])([A-Z])/g, '$1 $2'), value: overview[metric] })), ['metric', 'value'])] })
  return reports
}

export function assetRegisterReports(rows: ReportRow[], coverage: string): StaffReport[] {
  return [
    { title: 'Asset register', description: 'Asset identity, allocation and current operational state.', coverage, sections: [section('Assets', rows, ['assetNumber', 'name', 'branchName', 'category', 'status', 'registrationNumber'])] },
    { title: 'Fleet availability', description: 'Current status by asset. This snapshot does not calculate historical utilisation.', coverage, sections: [section('Status summary', [...new Set(rows.map(row => row.status))].map(status => ({ status, assets: rows.filter(row => row.status === status).length })), ['status', 'assets']), section('Availability detail', rows, ['assetNumber', 'name', 'branchName', 'status'])] },
    { title: 'Service and meter register', description: 'Recorded service dates and latest usage readings.', coverage, sections: [section('Service schedule', rows.map(row => ({ ...row, date: row.nextServiceDate })), ['assetNumber', 'name', 'date', 'currentMeterReading', 'meterUnit', 'status'])] },
  ]
}

export async function loadAssetRegisterReports(filters: Record<string, string | undefined>): Promise<StaffReport[]> {
  const rows: ReportRow[] = []
  for (let page = 1; ; page++) {
    const response = await api.get<ReportRow[]>('/assets', { params: { ...filters, page, pageSize: 100 } })
    rows.push(...response.data)
    if (response.data.length < 100) break
  }
  return assetRegisterReports(rows, `All ${rows.length} assets matching the current register filters within your access. Generated from current records.`)
}

export async function detailedAssetReports(id: string): Promise<StaffReport[]> {
  const profile = (await api.get(`/assets/${id}/profile`)).data
  let performance: ReportRow | null = null
  try { performance = (await api.get(`/assets/${id}/performance`)).data } catch (error) {
    const status = (error as { response?: { status: number } }).response?.status
    if (status !== 403) throw error
  }
  const a = profile.asset
  const fields = ['assetNumber', 'name', 'type', 'status', 'registrationNumber', 'serialNumber', 'vinOrChassisNumber', 'engineNumber', 'manufacturer', 'model', 'modelYear', 'ownershipType', 'acquisitionDate', 'currentLocation', 'currentMeterReading', 'meterUnit', 'nextServiceDate', 'insurancePolicyNumber', 'insuranceExpiry', 'warrantyExpiry']
  const identity = fields.map(field => ({ detail: field.replace(/([a-z])([A-Z])/g, '$1 $2'), value: a[field] }))
  identity.push(...['branch', 'division', 'assetCategory', 'serviceOffering'].map(field => ({ detail: field, value: a[field]?.name })))
  const sections = [
    section('Identity, ownership and compliance', identity, ['detail', 'value']),
    section('Category specifications', (a.attributeValues ?? []).map((row: ReportRow) => ({ specification: (row.attributeDefinition as ReportRow)?.name, value: row.value, unit: (row.attributeDefinition as ReportRow)?.unit })), ['specification', 'value', 'unit']),
    section('Current availability', [{ status: a.status, reason: profile.availability.unavailableReason, expectedAvailableAt: profile.availability.expectedAvailableAt }], ['status', 'reason', 'expectedAvailableAt']),
    section('Hire history', profile.availability.bookings.map((row: ReportRow) => ({ ...row, date: row.startAt })), ['reference', 'date', 'endAt', 'status']),
    section('Rental inspections and damage', (profile.rentalInspections ?? []).map((row: ReportRow) => ({ ...row, date: row.completedAt })), ['bookingNumber', 'date', 'type', 'conditionNotes', 'damageNotes', 'completedByName']),
    section('Asset inspections', profile.inspections.map((row: ReportRow) => ({ ...row, date: row.completedAt })), ['date', 'stage', 'outcome', 'meterReading', 'notes', 'completedByName']),
    section('Maintenance history', profile.maintenance.map((row: ReportRow) => ({ ...row, date: row.reportedAt })), ['jobNumber', 'date', 'serviceType', 'status', 'faultDescription', 'completedAt']),
    section('Meter readings', profile.meters.map((row: ReportRow) => ({ ...row, date: row.recordedAt })), ['date', 'type', 'reading', 'unit', 'fuelPercent', 'source']),
    section('Transfers', profile.availability.transfers.map((row: ReportRow) => ({ ...row, date: row.createdAt })), ['transferNumber', 'date', 'status', 'reason']),
    section('Lifecycle', profile.lifecycle.map((row: ReportRow) => ({ ...row, date: row.occurredAt })), ['date', 'type', 'fromStatus', 'toStatus', 'notes', 'recordedByName']),
    section('Document register', profile.documents.map((row: ReportRow) => ({ ...row, date: row.createdAt })), ['date', 'documentNumber', 'fileName', 'type', 'expiresOn']),
    section('Audit history', profile.audits.map((row: ReportRow) => ({ ...row, date: row.occurredAt })), ['date', 'action', 'summary', 'userName']),
  ]
  if (performance) {
    const financialFields = ['rentalRevenue', 'maintenanceExpense', 'operatingExpense', 'transferExpense', 'totalExpense', 'operatingProfit', 'acquisitionCost', 'currentBookValue', 'lifetimeNetAfterAcquisition']
    sections.splice(3, 0, section('Lifetime financial performance (FJD)', financialFields.map(metric => ({ metric: metric.replace(/([a-z])([A-Z])/g, '$1 $2'), value: performance![metric] })), ['metric', 'value']), section('Lifetime usage totals', ['rentalCount', 'rentalDays', 'inspectionCount'].map(metric => ({ metric: metric.replace(/([a-z])([A-Z])/g, '$1 $2'), value: performance![metric] })), ['metric', 'value']))
    sections.push(section('Maintenance expenditure (FJD)', (performance.maintenance as ReportRow[] ?? []).map(row => ({ ...row, date: row.reportedAt })), ['jobNumber', 'date', 'partsCost', 'labourCost', 'taxCost', 'actualCost', 'downtimeHours']), section('Operating expenditure (FJD)', (performance.costs as ReportRow[] ?? []).map(row => ({ ...row, date: row.occurredOn })), ['date', 'category', 'description', 'amount', 'supplier', 'referenceNumber']))
  }
  return [{ title: `${a.assetNumber} — Detailed asset report`, description: `${a.name} | ${a.branch?.name ?? 'Branch not recorded'} | ${a.division?.name ?? 'Division not recorded'}`, coverage: `Generated from the current asset profile. Bookings, maintenance, meters, transfers, documents, lifecycle and audit history include the latest 50 records each; inspection history includes all returned records. Financial figures are lifetime totals${performance ? '.' : ' and are excluded for your access level.'} Inspection photos and signatures remain in the asset profile.`, sections }]
}

export function financeReports(invoices: ReportRow[], payments: ReportRow[]): StaffReport[] {
  const coverage = 'Latest 200 invoices and 200 payments in your permitted branches. Totals relate only to these records. Amounts in FJD; this is an operational snapshot.'
  const invoiceRows: ReportRow[] = invoices.map(row => ({ ...row, date: row.issuedAt }))
  const paymentRows: ReportRow[] = payments.map(row => ({ ...row, date: row.createdAt }))
  const outstanding = invoiceRows.filter(row => Number(row.balanceDue) > 0).map(row => ({ ...row, ageDays: Math.max(0, Math.floor((Date.now() - Date.parse(String(row.issuedAt))) / 86400000)) }))
  return [
    { title: 'Invoice register', description: 'Billed amounts, payments allocated and remaining invoice balances.', coverage, sections: [section('Invoices', invoiceRows, ['invoiceNumber', 'date', 'customerName', 'branchName', 'total', 'amountPaid', 'balanceDue'])] },
    { title: 'Outstanding balances', description: 'Open balances with age measured from invoice issue, not contractual due date.', coverage, sections: [section('Unpaid invoices', outstanding, ['invoiceNumber', 'date', 'customerName', 'ageDays', 'balanceDue', 'status'])] },
    { title: 'Payment register', description: 'Receipts, refunds and bond transactions with method and status for reconciliation. Refunds are shown as positive amounts with their transaction type.', coverage, sections: [section('Payments', paymentRows, ['receiptNumber', 'date', 'customerName', 'type', 'method', 'amount', 'status'])] },
    { title: 'Bond transactions', description: 'Recorded bond collections and refunds. Use the rental settlement record for the current bond balance.', coverage, sections: [section('Collections and refunds', paymentRows.filter(row => ['Deposit', 'BondCollection', 'BondRefund'].includes(String(row.type))), ['receiptNumber', 'date', 'bookingNumber', 'customerName', 'type', 'amount', 'status'])] },
  ]
}
