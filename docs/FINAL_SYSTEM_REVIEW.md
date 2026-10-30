# CREMS final system review — 30 October 2026

## Scope and outcome

Reviewed backend controllers, authorization and session handling, booking and quotation transitions, pickup and return, payments and bonds, maintenance, asset availability and financial reporting, generated document data, and related frontend state and validation. The source inventory contains 40 controllers and 260 HTTP action declarations; this is an inventory, not a claim that every action was executed.

Corrected login OTP purpose checks, disabled-account verification, password reuse on recovery, invalid numeric enum handling, payment/invoice reconciliation, unauthorized lifecycle shortcuts, dispatch and personnel validation, and availability calculations. Return charges are now explicitly inserted rather than incorrectly treated as existing records. Maintenance supports named technicians and preserves concurrency and permission checks before assignment validation.

Financial changes itemize rental income, surcharges, maintenance expenditure, receipts, credits and bond settlement. Statement responses use explicit document fields to avoid entity-reference cycles and exclude void/draft invoices from account totals. Quotation totals respect configured division VAT and taxable charge flags. UI fixes cover recovered signatures, dialog resets, request timeout handling and frontend lint errors.

## Verification

- API automated suite: **475 passed, zero failed, zero skipped**, with all three opt-in disposable SQL integration checks enabled. Each SQL fixture creates a uniquely named test database and deletes that database in its cleanup; the working database is not its test target.
- Frontend automated suite: **59 passed across 10 files**.
- Frontend production build: passed.
- Frontend lint: zero errors, two Fast Refresh export warnings.
- Whitespace validation: `git diff --check` passed.
- Final API regression after the statement response change: **472 passed**, zero failed; the three SQL checks were not repeated in that run.

Commands: `dotnet test tests/CREMS.Api.Tests/CREMS.Api.Tests.csproj --no-restore`, enabling `CREMS_RUN_SQL_TESTS=1`, `CREMS_RUN_ORGANIZATION_SQL_TESTS=1`, and `CREMS_RUN_SETTINGS_SQL_TESTS=1` for SQL checks; `npm run build`, `npm run lint`, and `npm test -- --reporter=dot` in `src/crems-web`.

## Remaining verification and advisories

- Browser end-to-end coverage of every workflow and every API action has not been established by this review. Client UAT remains necessary, especially representative pickups, returns, printed PDFs and exported reports using the client's configuration.
- Windows installation and the client's actual SMTP delivery have not been exercised on this Mac. SMTP remains optional for installation, but email-based flows need functioning SMTP when used.
- NuGet could not retrieve vulnerability advisory data from api.nuget.org. Compilation and test success do not establish that dependency advisories are clear.
- Vite reports a shared JavaScript chunk of about 601 KB before compression (191 KB gzip). Two development Fast Refresh warnings remain in LicenceWorkflowDialog and ToastHost.
- This review does not certify production security, tax compliance or accounting policy. Confirm configured tax, bond, credit and payment policies with the client.

Changes remain local until explicitly committed and pushed.
