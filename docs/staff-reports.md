# Staff reporting

Reports are available through the outlined report button in Operations, Asset register,
Finance, and Reports & performance. The preview supports text search, dates, PDF and CSV.
Preview tables show ten records per page; downloads contain every matching record.

## Available reports

- Operations: overview briefing, deliveries/collections, outstanding tasks, quotations,
  approvals, customer cases, personnel assignments, alerts, parts, purchase orders and transfers.
- Asset register: all assets matching the register filters, current availability and
  service/meter records. All matching asset pages are fetched when opening the report.
- Asset profile: detailed identity, specifications, compliance dates, availability,
  hire history, inspections/damage, maintenance, meters, transfers, lifecycle, documents
  and audit history. Financial performance and cost detail are included only when the
  existing performance endpoint authorizes the caller.
- Finance: invoice register, outstanding balances, payment register and bond transactions.
- Reports & performance: asset profitability and recent audit export.

## Coverage and interpretation

The preview and PDF identify source coverage. Operations transaction lists generally
contain the latest 100 records; personnel assignments contain up to 150. Finance uses
the latest 200 invoices/payments. Asset profile histories generally contain the latest
50 records in each category, while inspections include all returned records. Audit
exports use the latest 100 events. These limits come from the existing endpoints.

Date filters apply to each row's primary Date column in Fiji time. Undated summaries
remain visible and financial performance is explicitly labelled as lifetime figures.
Invoice age is measured from issue date, not payment due date. Bond transaction reports
are registers, not a calculation of bonds currently held. Reports are generated snapshots,
not signed transaction documents or archived agreement versions. Inspection photographs
and signatures remain accessible through the asset profile.

## Verification

`tests/browser/report-workspace-browser.cjs` mounts the real report UI with fictional
records and intercepted asset APIs. It checks pagination, search, date validation,
PDF download, CSV escaping, mobile controls, and financial-access denial. It requires
a Vite development server and Playwright; see the existing browser-test README for setup.
PDF and screenshot QA outputs go to ignored `tmp/reports-qa/`.
