# Inspection browser regression checks

Start the frontend development server on http://localhost:5173.
Run from the repository root with Node and Playwright (Chrome installed).
If Playwright is not in node_modules, set PLAYWRIGHT_MODULE to its absolute package path.

    node tests/browser/inspection-photos-browser.cjs
    node tests/browser/inspection-workflow-browser.cjs
    node tests/browser/asset-inspection-browser.cjs

These tests mount the real React components through Vite. API writes are intercepted:
they verify browser interaction and submitted payloads without changing bookings,
billing customers, or sending emails. They do not verify database persistence.

Coverage includes opening the file chooser, decoded previews, adding/removing files,
corrupt-image feedback, resizing phone photos larger than 3 MB, complete guided
pickup and return submissions, signatures/acknowledgement, and photo evidence in
general asset inspections. Rental inspections are directed to Hire operations.

Run `node tests/browser/return-charges-browser.cjs` to verify automatic overdue
fees, restoring the calculated amount, waiving/editing the fee, and validation.
Set `CREMS_TEST_URL` to use a frontend preview other than http://localhost:5173.
The backend applies daily rate multiplied by overdue hours / 24, including partial hours,
rounded to cents. The return preview timestamp is retained through submission.
Bond requirements remain separate from rental invoice charges.

Run `node tests/browser/inspection-history-browser.cjs` to verify saved pre/post-hire details, photo enlargement, mobile layout, missing or malformed evidence, and access-error retry. API reads are mocked.

## Maintenance workflow checks

    node tests/browser/maintenance-workspace-browser.cjs

The original `maintenance-workflow-browser.cjs` command remains an alias. These mount
the real maintenance page, staff theme and shell against fictional intercepted API
responses. They cover explicit searchable asset selection, technician masters,
reasoned status actions, labour/fuel totals, stock issues and returns, evidence controls,
separate completion and safety release, stale edits, server filtering, session filters,
tablet/phone cards, unsaved-change protection and permission-controlled actions.
Screenshots are saved in `tmp/maintenance-browser` (override CREMS_TEST_ARTIFACTS).
Set CREMS_TEST_URL to choose the Vite port. Browser mocks do not verify persistence,
real attachment decoding, login, or external delivery.

For real SQL-backed HTTP and stock/concurrency checks, set CREMS_RUN_SQL_TESTS=1 and
provide CREMS_TEST_SQL_CONNECTION securely or use the API's user secrets, then run:

    dotnet test tests/CREMS.Api.Tests/CREMS.Api.Tests.csproj --filter FullyQualifiedName~MaintenanceSqlIntegrationTests

The SQL test creates and deletes only its uniquely named CremsMaintenanceTest_...
database. It uses real routes, cookie authentication, permissions, database queries
and transactions. Production startup seeding and email workers are excluded. It
checks paginated workspace/detail/schedule queries, stock contention, stale updates,
completion permissions, separate safety release, booking readiness, profitability,
returns at the original cost and duplicate preventive jobs. The account needs
permission to create a disposable test database.

Shared viewport checks:

    node tests/browser/stable-viewport-browser.cjs

Set VITE_TEST_URL for another frontend port. Measures header and content geometry around selects, nested selects, dialogs and temporary drawers at desktop, tablet and phone widths.

Maintenance tax coverage includes tax-exclusive and tax-inclusive totals, no-tax
mode, manual override reasons, division-rate snapshots, stock exclusions, saved
treatment settings, fuel charges, and financial permission masking. The SQL test runs the EF migration
chain before exercising the controllers.
