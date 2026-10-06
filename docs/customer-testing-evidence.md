# Customer regression testing evidence

Initial testing reviewed local main at `194ef7c` with the new tests and two UI fixes below. At that time, `origin/main` was two commits ahead (Swagger documentation/session tooling); those incoming commits were not part of the original screenshot evidence. Git author `Jayshil <jayshilkisun@outlook.com>` matches the local Git identity. No AGENTS.md was found; README.md engineering rules and tests/browser/README.md were reviewed.

## Integration verification before sharing

Before committing these changes, main was fast-forwarded to `11433a0` without conflicts. The combined code passed all 201 backend tests (0 failed, 0 skipped; runner duration 9 seconds), all 17 selected browser cases (the 12 customer cases plus 5 newly merged Swagger-panel cases; 0 failed, 0 skipped; 36.0007874 seconds), and all 9 existing Vitest tests (4.81 seconds). The production build succeeded (Vite build: 21.02 seconds).

The first normal backend build was blocked by a running API locking its executable. Tests then built successfully into an ignored temporary artifacts directory, without stopping the running API or changing shared build configuration:

```powershell
dotnet test tests/CREMS.Api.Tests/CREMS.Api.Tests.csproj --artifacts-path tmp/integration-test-artifacts --nologo
```

These integration totals include teammate tests and do not replace or alter the original screenshot counts recorded below.

## Contribution basis

| Work | Confirmed authored commits |
| --- | --- |
| Customer UI and catalogue | `203d7f1`, `2e7be47`, `ea6216b` |
| Customer/staff session and route isolation | `f92862a`, `5550e6f`, `efd87e6` |
| Booking navigation and Motors/Carptrac/equipment quotations | `bfbaad0`, `e07220e`, `2d8b3b3`, `75e415c`, `3a5bf43` |
| Public quotation API and diagnostics | `0793c1b` |
| Registration validation, account controls, preferences and initial licence OCR | `b1381e8` |

Later OCR name matching/session refinements (`5db0705`) and multi-recipient notification work (`71c222c`) are teammate contributions. Existing tests for these and other features ran in the whole-project regression suite; that suite's total is not a count of Jayshil's work alone. Notification features were not expanded as part of the focused customer tests.

## Actual successful runs

| Runner | Result | Runner-reported duration |
| --- | --- | --- |
| Entire xUnit backend suite | 189 passed, 0 failed | 10.9288 seconds total |
| Final focused customer xUnit suite (included in the overall 189) | 29 passed, 0 failed | 13.0941 seconds total |
| Customer browser suite: Playwright with Node's spec test reporter | 12 passed, 0 failed, 0 skipped | 24.9123986 seconds |
| Existing Vitest suite | 9 passed in 3 files | 35.25 seconds |
| TypeScript/Vite production build | Succeeded; 1450 modules transformed | Vite build: 20.54 seconds |

The final focused backend run strengthened the blocked-customer/missing-licence tests to check the specific rejection reasons. Times vary with machine load and include different runner overhead; do not add the focused 29 to the full backend 189.

## Coverage and fixes

New tests were prepared with Codex assistance, using the project's existing EF Core in-memory/controller and Playwright browser approaches.

- Registration: successful Identity user/customer creation, customer role, queued verification email and customer cookie; duplicate Identity/customer email; weak password; invalid name/email/phone/password metadata; browser password mismatch and duplicate-email feedback.
- Login/account: successful customer cookie and login session recording; incorrect password and disabled Identity; blocked/inactive customer session; anonymous controller access; browser login feedback, hydration, session expiry and keyboard logout.
- Customer dashboard/preferences: only the customer's booking list/details are returned; another customer's and missing bookings are not exposed; multiple preferences are deduplicated, persisted and cleared; browser identity fields are read-only and preference requests use the expected payload.
- Catalogue: visible asset returned; private division and maintenance asset excluded; invalid date range rejected; failed branch bootstrap and its automatic retry recover through Try again without expiring customer state.
- Booking/quotation: draft date updates and ownership restriction; equal/reversed dates and over-366-day request rejected without records/emails; confirmed date changes require branch review and reject duplicate requests; blocked customer and unverified vehicle licence rejected; submitted identity/licence replaced by authoritative customer records; real browser form steps test required phone, terms consent, booking/quotation flag and success response. Existing quotation tests exercise persisted quote/booking/customer-case records and cleanup after a simulated email-queue failure.
- Existing OCR parsing/confirmation tests ran because the initial OCR contribution is confirmed; later refinements remain shared work.

The browser tests exposed an authenticated-dashboard success message that was set but never rendered. CustomerPortalPage now displays it. CustomerSiteHeader now supplies the logout MenuItem directly to MUI Menu instead of wrapping it in a Fragment; the keyboard logout regression and assertions for MUI console errors pass.

Initial test-development failures were investigated and resolved: missing MVC services, positional-record metadata, exact result types, API interception matching frontend source modules, and required-field/selectors. No final failing cases were skipped or assertions weakened to obtain a pass.

## Setup and screenshot commands

Run from the repository root. Requires .NET 10 SDK, Node 22+, frontend dependencies and Google Chrome. SQL Server, a live API, real customer records and SMTP are not required for these focused tests. Dependencies were already present except Playwright, installed in the ignored temporary folder below.

One-time setup if dependencies are missing:

```powershell
npm --prefix src/crems-web ci
npm install --prefix tmp/customer-test-tools --no-save --package-lock=false playwright
dotnet restore tests/CREMS.Api.Tests/CREMS.Api.Tests.csproj
```

Start a Vite server in a separate terminal and leave it running while taking browser evidence:

```powershell
npm --prefix src/crems-web run dev -- --host 127.0.0.1 --port 5174
```

Screenshot command for the 12 readable customer UI cases, with native PASS/FAIL symbols, totals and timings:

```powershell
node --test --test-reporter=spec tests/browser/customer-journey-regression.cjs
```

For another existing Vite URL, set CREMS_TEST_URL in the test terminal. PLAYWRIGHT_MODULE can point to another installed Playwright package, as in the existing browser test approach. The scripts have no PC-specific absolute paths. Color follows Node's terminal support; in PowerShell it can be explicitly enabled for a screenshot with `$env:FORCE_COLOR = '1'` before running Node.

Screenshot command for the focused 29 controller cases:

```powershell
dotnet test tests/CREMS.Api.Tests/CREMS.Api.Tests.csproj --filter FullyQualifiedName~CustomerJourneyRegressionTests --logger "console;verbosity=normal" --nologo
```

Existing frontend tests, showing individual names:

```powershell
npm --prefix src/crems-web test -- --reporter=verbose
```

Whole-project backend regression and frontend build:

```powershell
dotnet test tests/CREMS.Api.Tests/CREMS.Api.Tests.csproj --logger "console;verbosity=normal" --nologo
npm --prefix src/crems-web run build
```

Genuine saved backend output/results from this run are in ignored `tmp/test-results/backend-output.log`, `customer-output.log`, `crems-regression.trx` and `customer-regression.trx`. These are runner output, not invented screenshot text. Rerunning commands creates fresh terminal output; execution times need not match the values above.

## Limits

Controller tests use real Identity services and ephemeral cookie protection with fresh EF Core in-memory databases. Direct controller calls do not exercise HTTP routing/model-binding/authorization filters, SQL Server constraints, real transactions, concurrent booking races or a deployed system. Record annotation tests inspect MVC's constructor-parameter validation metadata; they do not send invalid requests through a live HTTP pipeline.

Browser tests mount the real React components through Vite, but every `/api/` request is intercepted with synthetic `.test` identities and fake data. They verify UI interaction and payloads, not backend persistence or authorization enforcement. Queued verification/booking emails are asserted in memory, not delivered. Native image OCR extraction, macOS behaviour, production deployment and unrelated inspection browser scripts were not tested. All selected tests ran successfully; none remain blocked.

## Two-sentence artefact caption

This terminal evidence shows automated regression testing of my CREMS customer login, registration, dashboard, catalogue, rental preferences, booking and quotation workflows, including invalid inputs and customer ownership restrictions. New tests were prepared with Codex assistance using isolated in-memory data and mocked browser APIs, so the results demonstrate the tested behaviours without claiming live database, email delivery or macOS verification.
