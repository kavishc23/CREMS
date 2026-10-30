import { api } from '../api/client'
import { reportSection as section, type ReportRow, type StaffReport } from './reportDocument'

export function operationsReports(workspace: Record<string, ReportRow[]>, overview?: Record<string, number> | null): StaffReport[] {
  const definitions: [string, string, string[], string[]][] = [
    ['dispatches', 'Deliveries and collections', ['dispatchNumber', 'date', 'type', 'status', 'address', 'assignedDriver'], ['dispatchNumber', 'date', 'completedAt', 'type', 'status', 'address', 'assignedDriver', 'bookingId', 'branchId', 'id']],
    ['tasks', 'Outstanding work', ['title', 'date', 'priority', 'description', 'isCompleted'], ['title', 'date', 'category', 'priority', 'description', 'isCompleted', 'sourceEntityType', 'sourceEntityId', 'branchId', 'id']],
    ['quotes', 'Quotation register', ['quoteNumber', 'date', 'status', 'validUntil', 'total'], ['quoteNumber', 'date', 'validUntil', 'status', 'jobSite', 'purchaseOrderNumber', 'subtotal', 'discount', 'tax', 'total', 'customerId', 'branchId', 'divisionId', 'convertedBookingId', 'id']],
    ['approvals', 'Approval register', ['requestNumber', 'date', 'type', 'status', 'amount', 'reason'], ['requestNumber', 'date', 'type', 'entityType', 'status', 'amount', 'reason', 'currentStage', 'totalStages', 'decisionNote', 'decidedAt', 'decidedByName', 'id']],
    ['cases', 'Customer cases', ['caseNumber', 'date', 'type', 'status', 'subject', 'priority'], ['caseNumber', 'date', 'type', 'status', 'subject', 'description', 'priority', 'dueAt', 'resolution', 'customerId', 'branchId', 'id']],
    ['assignments', 'Personnel assignments', ['personnelName', 'date', 'endAt', 'customerHourlyRate'], ['personnelName', 'date', 'endAt', 'customerHourlyRate', 'bookingId', 'id']],
    ['alerts', 'Business alerts', ['title', 'date', 'category', 'priority', 'message'], ['title', 'date', 'category', 'priority', 'message', 'entityType', 'entityId', 'branchId', 'divisionId', 'id']],
    ['parts', 'Parts stock', ['partNumber', 'name', 'quantityOnHand', 'quantityAllocated', 'reorderLevel'], ['partNumber', 'name', 'quantityOnHand', 'quantityAllocated', 'quantityOnOrder', 'reorderLevel', 'unitCost', 'supplier', 'branchId', 'id']],
    ['purchaseOrders', 'Purchase order register', ['purchaseOrderNumber', 'date', 'supplier', 'status', 'total'], ['purchaseOrderNumber', 'date', 'supplier', 'status', 'total', 'branchId', 'requestedByUserId', 'approvedByUserId', 'id']],
    ['transfers', 'Asset transfers', ['transferNumber', 'date', 'status', 'reason'], ['transferNumber', 'date', 'status', 'reason', 'assetId', 'fromBranchId', 'toBranchId', 'transferCost', 'id']],
  ]
  const reports = definitions.filter(([key]) => workspace[key] !== undefined).map(([key, title, columns, csvColumns]) => ({ title, description: 'Operational records for your permitted branches and divisions. Monetary amounts are in FJD.', coverage: 'Current operations workspace snapshot. Transaction lists contain up to 100 records; personnel assignments contain up to 150. This is a register, not a signed transaction document.', sections: [section(title, (workspace[key] ?? []).map(row => ({ ...row, date: row.scheduledAt ?? row.dueAt ?? row.startAt ?? row.raisedAt ?? row.createdAt })), columns, csvColumns)] }))
  if (overview) reports.unshift({ title: 'Operations briefing', description: 'Current workload, overdue hires and operational exceptions.', coverage: 'Current scoped operations overview. Values reflect the time this module was loaded.', sections: [section('Operational summary', ['activeRentals', 'overdueRentals', 'todayDispatches', 'pendingApprovals', 'pendingQuotes', 'openCases', 'activeTasks', 'lowStockParts', 'assetCount', 'rentedAssets', 'utilizationPercent', 'outstandingReceivables'].map(metric => ({ metric: metric.replace(/([a-z])([A-Z])/g, '$1 $2'), value: overview[metric] })), ['metric', 'value'])] })
  return reports
}

export function assetRegisterReports(rows: ReportRow[], coverage: string): StaffReport[] {
  const assetDetail = ['assetNumber', 'name', 'type', 'category', 'status', 'isActive', 'branchName', 'divisionName',
    'currentLocation', 'registrationNumber', 'serialNumber', 'vinOrChassisNumber', 'engineNumber',
    'manufacturer', 'model', 'modelYear', 'ownershipType', 'acquisitionDate', 'acquisitionCost',
    'currentBookValue', 'dailyRate', 'defaultBondAmount', 'inheritBond', 'personnelRequirement',
    'requiresDelivery', 'currentMeterReading', 'meterUnit', 'nextServiceDate',
    'insurancePolicyNumber', 'insuranceExpiry', 'warrantyExpiry', 'branchId', 'divisionId',
    'serviceOfferingId', 'assetCategoryId', 'id']
  return [
    { title: 'Asset register', description: 'Asset identity, allocation and current operational state. CSV includes ownership, valuation, compliance, hire configuration and source identifiers.', coverage, sections: [section('Assets', rows, ['assetNumber', 'name', 'branchName', 'category', 'status', 'registrationNumber'], assetDetail)] },
    { title: 'Fleet availability', description: 'Current status by asset. This snapshot does not calculate historical utilisation.', coverage, sections: [section('Status summary', [...new Set(rows.map(row => row.status))].map(status => ({ status, assets: rows.filter(row => row.status === status).length })), ['status', 'assets']), section('Availability detail', rows, ['assetNumber', 'name', 'branchName', 'status'], ['assetNumber', 'name', 'type', 'category', 'status', 'isActive', 'branchName', 'divisionName', 'currentLocation', 'dailyRate', 'nextServiceDate', 'insuranceExpiry', 'currentMeterReading', 'meterUnit', 'id'])] },
    { title: 'Service and meter register', description: 'Recorded service dates and latest usage readings.', coverage, sections: [section('Service schedule', rows.map(row => ({ ...row, date: row.nextServiceDate })), ['assetNumber', 'name', 'date', 'currentMeterReading', 'meterUnit', 'status'], ['assetNumber', 'name', 'branchName', 'divisionName', 'category', 'status', 'date', 'currentMeterReading', 'meterUnit', 'insuranceExpiry', 'warrantyExpiry', 'registrationNumber', 'serialNumber', 'id'])] },
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
    section('Hire history', profile.availability.bookings.map((row: ReportRow) => ({ ...row, date: row.startAt })), ['reference', 'date', 'endAt', 'status'], ['reference', 'date', 'endAt', 'status', 'customerName', 'bookingId', 'dailyRate']),
    section('Rental inspections and damage', (profile.rentalInspections ?? []).map((row: ReportRow) => ({ ...row, date: row.completedAt })), ['bookingNumber', 'date', 'type', 'conditionNotes', 'damageNotes', 'completedByName'], ['bookingNumber', 'date', 'type', 'conditionNotes', 'damageNotes', 'meterReading', 'fuelLevelPercent', 'identificationVerified', 'driverLicenceVerified', 'paymentVerified', 'signatureName', 'completedByName', 'bookingId', 'id']),
    section('Asset inspections', profile.inspections.map((row: ReportRow) => ({ ...row, date: row.completedAt })), ['date', 'stage', 'outcome', 'meterReading', 'notes', 'completedByName'], ['date', 'stage', 'outcome', 'meterReading', 'fuelPercent', 'notes', 'damageMapJson', 'customerSignatureName', 'staffSignatureName', 'completedByName', 'bookingId', 'id']),
    section('Maintenance history', profile.maintenance.map((row: ReportRow) => ({ ...row, date: row.reportedAt })), ['jobNumber', 'date', 'serviceType', 'status', 'faultDescription', 'completedAt'], ['jobNumber', 'date', 'completedAt', 'serviceType', 'status', 'priority', 'faultDescription', 'supplier', 'estimatedCost', 'actualCost', 'downtimeHours', 'id']),
    section('Meter readings', profile.meters.map((row: ReportRow) => ({ ...row, date: row.recordedAt })), ['date', 'type', 'reading', 'unit', 'fuelPercent', 'source']),
    section('Transfers', profile.availability.transfers.map((row: ReportRow) => ({ ...row, date: row.createdAt })), ['transferNumber', 'date', 'status', 'reason']),
    section('Lifecycle', profile.lifecycle.map((row: ReportRow) => ({ ...row, date: row.occurredAt })), ['date', 'type', 'fromStatus', 'toStatus', 'notes', 'recordedByName']),
    section('Document register', profile.documents.map((row: ReportRow) => ({ ...row, date: row.createdAt })), ['date', 'documentNumber', 'fileName', 'type', 'expiresOn']),
    section('Audit history', profile.audits.map((row: ReportRow) => ({ ...row, date: row.occurredAt })), ['date', 'action', 'summary', 'userName']),
  ]
  if (performance) {
    const financialFields = ['rentalRevenue', 'maintenanceExpense', 'operatingExpense', 'transferExpense', 'totalExpense', 'operatingProfit', 'acquisitionCost', 'currentBookValue', 'lifetimeNetAfterAcquisition']
    sections.splice(3, 0, section('Lifetime financial performance (FJD)', financialFields.map(metric => ({ metric: metric.replace(/([a-z])([A-Z])/g, '$1 $2'), value: performance![metric] })), ['metric', 'value']), section('Lifetime usage totals', ['rentalCount', 'rentalDays', 'inspectionCount'].map(metric => ({ metric: metric.replace(/([a-z])([A-Z])/g, '$1 $2'), value: performance![metric] })), ['metric', 'value']))
    sections.push(
      section('Asset financial ledger (FJD)', (performance.ledger as ReportRow[] ?? []), ['date', 'reference', 'category', 'description', 'revenue', 'expense'], ['date', 'reference', 'relatedReference', 'category', 'description', 'quantity', 'rate', 'revenue', 'expense', 'recordedBy', 'id']),
      section('Rental revenue trace (FJD)', (performance.rentals as ReportRow[] ?? []), ['bookingNumber', 'customerName', 'rentalCharge', 'services', 'assetRevenue', 'invoiceNumber'], ['bookingNumber', 'customerName', 'status', 'rentalCharge', 'services', 'discount', 'assetRevenue', 'invoiceNumber', 'bookingNet', 'bookingVat', 'bookingTotal', 'invoiceTotal', 'paid', 'balance', 'bondAmountHeld', 'bondDeductionAmount', 'bondRefundAmount', 'id']),
      section('Booking receipts including separate bonds (FJD)', (performance.payments as ReportRow[] ?? []).map(row => ({ ...row, date: row.createdAt })), ['bookingNumber', 'receiptNumber', 'date', 'type', 'amount', 'status'], ['bookingNumber', 'receiptNumber', 'date', 'type', 'method', 'amount', 'status', 'recordedByName', 'note', 'id']))
    sections.push(section('Maintenance expenditure (FJD)', (performance.maintenance as ReportRow[] ?? []).map(row => ({ ...row, date: row.reportedAt })), ['jobNumber', 'date', 'serviceType', 'actualCost', 'invoiceNumber'], ['jobNumber', 'date', 'serviceType', 'status', 'partsCost', 'labourCost', 'transportCost', 'externalServiceCost', 'fuelCost', 'otherCost', 'taxMode', 'taxRate', 'taxCost', 'actualCost', 'downtimeHours', 'invoiceNumber', 'id']), section('Operating expenditure (FJD)', (performance.costs as ReportRow[] ?? []).map(row => ({ ...row, date: row.occurredOn })), ['date', 'category', 'description', 'amount', 'supplier', 'referenceNumber']))
  }
  return [{ title: `${a.assetNumber} — Detailed asset report`, description: `${a.name} | ${a.branch?.name ?? 'Branch not recorded'} | ${a.division?.name ?? 'Division not recorded'}`, coverage: `Generated from the current asset profile. Bookings, maintenance, meters, transfers, documents, lifecycle and audit history include the latest 50 records each; inspection history includes all returned records. Financial figures are lifetime contracted revenue excluding VAT and bonds. Booking receipt and invoice totals are full booking amounts; shared asset revenue and costs are allocated equally. Actual staffing costs replace quoted staffing costs when available${performance ? '.' : ' and are excluded for your access level.'} Inspection photos and signatures remain in the asset profile.`, sections }]
}

export function financeReports(invoices: ReportRow[], payments: ReportRow[]): StaffReport[] {
  const coverage = 'Latest 200 invoices and 200 payments in your permitted branches. Totals relate only to these records. Amounts in FJD; this is an operational snapshot.'
  const invoiceRows: ReportRow[] = invoices.map(row => ({ ...row, date: row.issuedAt }))
  const paymentRows: ReportRow[] = payments.map(row => ({ ...row, date: row.createdAt }))
  const outstanding = invoiceRows.filter(row => Number(row.balanceDue) > 0).map(row => ({ ...row, ageDays: Math.max(0, Math.floor((Date.now() - Date.parse(String(row.issuedAt))) / 86400000)) }))
  return [
    { title: 'Invoice register', description: 'Billed amounts, payments allocated and remaining invoice balances.', coverage, sections: [section('Invoices', invoiceRows, ['invoiceNumber', 'date', 'customerName', 'branchName', 'total', 'amountPaid', 'balanceDue'], ['invoiceNumber', 'date', 'bookingNumber', 'customerName', 'branchName', 'status', 'total', 'amountPaid', 'balanceDue', 'bookingId', 'id'])] },
    { title: 'Outstanding balances', description: 'Open balances with age measured from invoice issue, not contractual due date.', coverage, sections: [section('Unpaid invoices', outstanding, ['invoiceNumber', 'date', 'customerName', 'ageDays', 'balanceDue', 'status'], ['invoiceNumber', 'date', 'bookingNumber', 'customerName', 'branchName', 'status', 'ageDays', 'total', 'amountPaid', 'balanceDue', 'bookingId', 'id'])] },
    { title: 'Payment register', description: 'Receipts, refunds and bond transactions with method and status for reconciliation. Refunds are shown as positive amounts with their transaction type.', coverage, sections: [section('Payments', paymentRows, ['receiptNumber', 'date', 'customerName', 'type', 'method', 'amount', 'status'], ['receiptNumber', 'date', 'bookingNumber', 'customerName', 'branchName', 'type', 'method', 'status', 'amount', 'bookingId', 'id'])] },
    { title: 'Bond transactions', description: 'Recorded bond collections and refunds. Use the rental settlement record for the current bond balance.', coverage, sections: [section('Collections and refunds', paymentRows.filter(row => ['Deposit', 'BondCollection', 'BondRefund'].includes(String(row.type))), ['receiptNumber', 'date', 'bookingNumber', 'customerName', 'type', 'amount', 'status'], ['receiptNumber', 'date', 'bookingNumber', 'customerName', 'branchName', 'type', 'method', 'status', 'amount', 'bookingId', 'id'])] },
  ]
}
