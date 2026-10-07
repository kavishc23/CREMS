# Maintenance and related workflow audit - 8 October 2026 (Fiji)

Reviewed main at 4c9c814. This was a validation and status review; application code and operational records were not changed.

## Results

- Existing API suite: 279 passed, 0 failed, 0 skipped, including an isolated SQL database and the migration chain.
- Frontend unit tests: 23 passed.
- Eight browser suites passed: maintenance workspace, inspection photos, pickup/return workflow, general asset inspection, inspection history, booking pre-hire, return charges, and stable viewport.
- Production frontend build and maintenance ESLint passed.
- Local API health passed and the maintenance workspace route is present in its OpenAPI document.
- Three additional isolated controller probes: one passed and two exposed defects listed below. The existing green suite therefore does not establish that every scenario works.

Browser suites render real components with intercepted API requests; database and HTTP persistence are covered separately by the SQL test. External email delivery, production login, load and deployment were not tested. The viewport test logged development hot-reload WebSocket warnings but all geometry assertions passed.

## Confirmed defects (corrected in the working tree)

1. Invoice-total entry retains hidden labour hours/rate. Enter 2 hours at 30, save detailed costs, switch to total without breakdown, then enter 100. The form retains the hours/rate; Update recalculates LabourCost before considering entry mode. The actual saved total is 60, not 100. Fix both request construction and server cost-mode precedence; preserve issued-stock reconciliation. Location: MaintenanceWorkspace.tsx payload and MaintenanceJobsController.cs Update.
2. The API accepts Completed -> Cancelled after safety release. The existing UI offers reopening, but a direct authorized update returns 204 instead of rejecting this transition; it clears CompletedAt while retaining the release metadata. This can rewrite history and completed-cost metrics. Enforce the intended transition rules on the server and require reopening with a reason before changing closed work. Location: MaintenanceJobsController.cs Update.

Diagnostic probes were run against isolated in-memory fixtures, not the user's maintenance jobs. Their source is retained locally in tmp/maintenance-status-audit/MaintenanceAuditProbes.cs (outside the default test project) for reproducing the findings.

## Status assessment

Current stored job statuses are Open, InProgress, WaitingForParts, Completed and Cancelled. Safety release is separately recorded. This separation should remain.

- Open: logged; work has not explicitly started. Assignment, costs and saving details do not start work.
- In progress: technician has started or resumed work.
- Waiting for parts: repair paused with a recorded reason.
- Completed: repair work finished; asset still requires safety release.
- Cancelled: work cancelled; history and incurred costs remain, and cancellation alone does not certify safety.
- Safety passed / released: show separately from job status, including the release date. An old released job does not prove an asset is available today.

Recommended presentation improvements:
- Label Open as Open / not started (or retain Open with explanatory help).
- Show Completed / awaiting safety check clearly in the register, as well as in the drawer.
- Keep Cancelled visible after release; currently Returned to service replaces both Completed and Cancelled in the status chip.
- Use Resume work when returning from Waiting for parts.
- Make job status, historical safety release, and current asset availability distinct; use consistent colours on desktop and mobile.
- Treat Awaiting safety check counts as jobs, since several closed jobs can belong to one asset and one release can cover them together.

## Read-only local data check

25 jobs: 16 Open, 2 In progress, 2 Waiting for parts, 4 Completed, 1 Cancelled. Two completed jobs are released; three closed jobs await release. No active job has a release timestamp. The screenshot's MNT-2026-B29C72 has actual cost 550, with StartedAt, CompletedAt and ReleasedAt all empty: Open is the recorded and expected state until Start work is used.

## Covered workflows

Creation and edits; references and permissions; stale versions; tax treatments and override reasons; parts issues/returns and original cost; stock concurrency; repair completion and separate safety release; multiple repair blockers; inspection/damage handoffs; preventive date/meter service; transfer restrictions; booking readiness; expense reporting; evidence handling; filters, pagination and responsive layout. See the test sources and previous validation report for scenario detail.


## Corrections and financial visibility follow-up

- Invoice-only requests clear hidden expense inputs and labour hours/rate on the frontend and server. The entered invoice total wins; the inventory minimum is still checked.
- Completed work cannot change directly to Cancelled, before or after safety release. Reopening with a reason clears release metadata and makes the asset unavailable again.
- Shared status rendering keeps job status visible separately from the dated safety release. Open reads Open / not started; completed/cancelled unreleased jobs show Awaiting safety check. Waiting jobs offer Resume work. The summary explicitly counts jobs awaiting safety checks.
- Regression tests cover the original failures, reopening/cancellation, zero invoice totals, stock-cost protection, and propagation of a corrected invoice total into asset expenses and profit.

Where financial information is shown:

| Location | Information |
| --- | --- |
| Maintenance job > Expenses | Expense breakdown, invoice total, tax and estimate |
| Maintenance job > Asset history | Total recorded costs and completed-job costs, book value comparison |
| Reports & performance > Asset profit and loss | Asset revenue, aggregate expenses, gross profit, margin, overall totals, CSV export |
| Finance | Customer invoices, payments, outstanding balances and refundable bonds |

The asset profit API includes recorded maintenance, asset cost entries, asset-linked booking charge costs, and personnel timesheets. Active/cancelled maintenance costs are retained; estimates are excluded. The report does not automatically represent all company overhead or accounting net profit. It displays aggregate expense per asset, not the API's individual expense categories or monthly trend.

Reporting limitations found by code inspection (not changed as part of the maintenance fixes):
- Booking charges without an AssetId are omitted from asset profitability.
- Booking charges are not filtered by booking status, unlike base rental revenue; cancelled/unconverted booking charges can therefore contribute.
- Personnel costs are assigned in full to each asset on a multi-asset booking, which can double-count them in the asset totals.
- Monthly maintenance expense falls back to zero when ActualCost is null, whereas asset totals fall back to the stored breakdown. This can make legacy records inconsistent between the two calculations.

These limitations mean the current asset report should not be described as a fully reconciled company-wide profit statement. A separate reporting correction needs an explicit allocation rule for shared booking costs and unassigned charges.

Fix validation: 284 API tests passed (SQL enabled), 23 frontend tests passed, production build and maintenance lint passed, and five relevant browser suites passed (maintenance, pickup/return, general asset inspection, booking pre-hire, return charges).
