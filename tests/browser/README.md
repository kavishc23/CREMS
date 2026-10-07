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

    node tests/browser/maintenance-workflow-browser.cjs

These mount the real maintenance page with intercepted API requests and fictional data.
They cover eligible assets, service alerts, creation, status selection, notes, cost
breakdowns (including clearing to zero and manual invoice totals), attachment controls,
search, history and the mobile edit dialog. API authorization, historical meter/schedule
updates, related references and real document decoding are covered by the .NET tests.

The maintenance browser check also verifies stale-form conflict feedback, stock issues,
unused-parts returns and stock movement history. For SQL-backed
HTTP and concurrency checks, set `CREMS_RUN_SQL_TESTS=1` and provide
`CREMS_TEST_SQL_CONNECTION` securely (or use the API's user secrets), then run:

    dotnet test tests/CREMS.Api.Tests/CREMS.Api.Tests.csproj --filter FullyQualifiedName~MaintenanceSqlIntegrationTests

The SQL test creates and deletes its own uniquely named `CremsMaintenanceTest_...`
database. The account needs permission to create test databases. It uses real controller
routes, cookie authentication and authorization policies; production login, startup
seeding and email workers are excluded. It checks stock contention, stale updates,
completion permissions, asset release, expense reporting, concurrent returns at the
original issue cost, and duplicate preventive jobs.

Maintenance tax coverage includes tax-exclusive and tax-inclusive totals, no-tax
mode, manual override reasons, division-rate snapshots, stock exclusions, saved
treatment settings, and explanatory tab labels. The SQL test runs the EF migration
chain before exercising the controllers.
