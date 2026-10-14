# Asset profitability corrections - 8 October 2026

## Changes

- Base rental revenue, booking charges and booking personnel costs consistently use active (ConvertedToRental) and completed bookings. Planned, expired and cancelled booking charges no longer inflate this report.
- Charges with an explicit asset remain assigned to it. Booking-wide charges and personnel timesheet costs are split equally across distinct booking assets. Duplicate item rows do not increase an asset's allocation share.
- Allocation rounds to cents and distributes remaining cents by sorted asset ID. It happens before branch/division visibility filtering, so scoped reports retain only their original shares and cannot inherit hidden asset costs.
- Eligible bookings with no assets contribute to an administrator-only unallocated balance, shown in overall totals, the report UI, monthly activity and CSV. Restricted users cannot see this balance because its division cannot be inferred.
- One set of entries supplies the asset and monthly totals. Maintenance uses ActualCost or the same legacy breakdown fallback, including inclusive-tax handling; estimates are excluded. Recorded maintenance costs survive job cancellation.
- Monthly grouping uses Fiji dates; personnel costs use WorkDate instead of the booking item creation month. Base rentals/charges use record creation dates, maintenance uses its reported date, and direct costs use OccurredOn. Monthly display is the last 12 months, while summary totals are all-time.
- Reports & performance now shows maintenance, other asset costs, booking charges and personnel separately, plus monthly recorded activity. The CSV serializer now uses the camel-case names its export reader expects.

## Scope

This remains an operational asset contribution report, not a general ledger, cash report or reconciled company net-profit statement. Booking-level discounts, AdditionalCharges adjustments, tax reconciliation, depreciation and unrecorded company overhead are not included. The report UI states its boundary. Actual payment and invoice balances remain in Finance. No historical records or schema migrations were changed.

## Verification

Regression cases cover positive/negative penny allocation, duplicate booking items, scoped cross-branch assets, active/completed versus other booking statuses, shared and unallocated charges/personnel, legacy maintenance costs, excluded estimates, monthly work dates and CSV amounts. A browser test verifies the new columns, unallocated balance, monthly figures, download and phone layout with intercepted API data. The full API suite also exercises real SQL migration and report queries in an isolated database.

Results: 289 API tests passed with SQL enabled, profitability browser test passed, production frontend build passed, and ReportsPage ESLint passed.

Merge reconciliation preserves main's maintenance filter popover, header save buttons, footer workflow actions and Mac OCR support. Job status, Resume work and unsaved-change feedback remain available. Combined-tree API validation: 289 passed; frontend unit tests: 23 passed; production build and maintenance/report lint passed.
