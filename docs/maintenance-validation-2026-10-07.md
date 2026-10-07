# Maintenance validation — 7 October 2026 (Pacific/Fiji)

## Result

All checks listed below passed after correcting the findings. This is validation of the local working tree, including uncommitted maintenance changes, not certification of a production deployment.

| Check | Result |
| --- | --- |
| Complete API regression suite | 256 passed, 0 failed, 0 skipped |
| Frontend unit tests | 22 passed |
| Maintenance browser workflow | Passed |
| Inspection photo handling | Passed |
| Complete pickup and return browser workflow | Passed |
| General asset inspection browser workflow | Passed |
| Inspection history browser workflow | Passed |
| Booking pre-hire preparation and reload | Passed |
| Return charges browser workflow | Passed |
| Frontend production build | Passed |
| Maintenance UI and tax helper ESLint checks | Passed |
| Git patch whitespace check | Passed |

## Coverage

- Maintenance creation, status transitions, cancellation, reopening, multiple repairs on one asset, completion permissions, stale-edit conflicts, historical schedules/meters, supplier/parent references and cycle rejection.
- Tax-exclusive, tax-inclusive, no-tax and manual tax; override reasons and audit records; original invoice totals; stored settings; taxable expense selection; inventory exclusions; decimal precision and half-cent rounding.
- Whole-unit stock validation, allocations, insufficient stock, branch/division access, closed-job issue restrictions, original-cost returns, over-return prevention, simultaneous issue/return attempts and tax-inclusive stock movements.
- Failed inspections and damaged rental returns into maintenance, separate tracking of new return damage when another repair exists, duplicate-return rejection and asset release with outstanding work.
- Preventive date/meter thresholds, Fiji dates, readings crossing thresholds while hired, duplicate job prevention and preservation of existing service schedules.
- Blocking transfers with open repairs, completing a transfer after repair closure, retaining historical branch ownership and rejecting reopening at the old branch.
- Maintenance expense reporting, costs retained on cancelled work and division-scoped dashboard maintenance costs.
- Image/PDF attachment decoding, download round trips, corrupt/truncated files, invalid categories/extensions, size limits and attachment access scope.
- Browser search, filters/history, explanatory tab labels, expense previews, stock movement controls, attachment UI, stale-save messages and mobile dialogs.
- Related pickup/return identification, scan checks, inspection checklists, signatures, photo evidence, saved pre-hire preparation, condition-photo comparison, return charges and bond handling.

## Findings corrected during this audit

1. **Tax preview rounding:** JavaScript floating-point arithmetic could display tax one cent below the backend calculation (for example, 17.08 at a configured 12.5% rate). The preview now uses integer cents and integer rate arithmetic. Added six frontend regression cases.
2. **New damage with an existing repair:** rental return previously skipped creating a new fault job if the asset already had open maintenance. Each newly reported return fault now receives a separate job. Added four combinations of damage/existing-repair tests, including repeated-return rejection.
3. **Return condition comparison:** restored before/after photo comparison to the active return workflow. Damage locations require explanatory notes, and the outcome label now reflects Maintenance for damage and conditional availability otherwise.
4. **Browser fixture drift:** updated pickup tests to follow the current identity-check-first workflow and current return steps. Photo, signature, damage-note and accessory assertions remain covered.

Added a repair-to-transfer regression and strengthened SQL HTTP coverage for completed-job return permissions and tax-inclusive inventory issue/return totals.

## Test boundaries

- The SQL test uses actual controllers, cookie authentication, authorization policies, EF migrations and SQL Server in a uniquely named temporary database. That database is removed after the test.
- Browser scripts render real React components but intercept API requests with fictional data. They validate interaction and payloads; database persistence is tested separately by the SQL suite.
- No real customer messages, payments, stock issues or rental transactions were created by these tests.
- Production identity-provider login, actual email delivery, backup restoration and production-scale load were not exercised. Notification routing/copy and session behavior are covered by the existing backend tests, not by live external delivery tests.
- Lint was checked for the maintenance files; this report does not claim a clean app-wide lint run.

See `tests/browser/README.md` for browser and isolated SQL test instructions.
