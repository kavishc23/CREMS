# Rental defaults configuration

All seven controls under System configuration > Rental defaults are validated by the API. Changes are audited and take effect without restarting the API after this code has been deployed. Existing documents and existing lockout expiry times are not rewritten.

| Setting | Accepted values | Effect |
| --- | --- | --- |
| Booking prefix | 1-12 ASCII letters, digits or hyphens; starts with a letter | Uppercased prefix for new direct bookings and bookings converted from quotes. Quote request references retain REQ. |
| Agreement prefix | Same prefix rules | Used when a rental agreement is signed. |
| Late return multiplier | Decimal from 0 to 10 | Multiplies the agreed daily rate prorated by overdue time. Zero disables calculated late fees. An explicit staff override still takes precedence. |
| VAT percentage | Decimal from 0 to 100 | Saving applies to every division for new requests. Existing booking tax rates remain unchanged. Division-specific overrides can be set afterwards. |
| Lockout duration | Whole minutes from 1 to 1440 | Duration of newly triggered password lockouts. |
| Failed password threshold | Whole attempts from 1 to 100 | Checked at the next password sign-in, including the current failure count. Does not configure MFA-code limits. |
| Retention review age | Whole years from 1 to 100 | Flags old closed bookings and audit events for administrator review. No automatic archive or deletion. |

Retention review is restricted by the existing system-administration policy. Completed, cancelled and expired bookings qualify only when their last update (or creation date if never updated) and every scheduled rental end predate the cutoff. Active rentals are excluded. Audit events use their occurrence date. Review related financial records and applicable retention obligations before any separate disposal decision. Each page contains up to 25 bookings and 25 audit events.

The form preserves unsaved changes in other settings when one setting is saved. Validation errors are shown using the API response. Missing settings have defaults and are created on first save; malformed legacy values use safe runtime defaults until corrected.

## Validation

- Backend tests: RentalSettingsTests covers persisted values, invalid input, missing defaults, unique reference generation, actual Identity password lockout behavior, retention filtering and pagination.
- CustomerJourneyRegressionTests verifies that public booking references use the saved prefix.
- ReturnChargesAndBondTests verifies saved multipliers, fee overrides, resulting invoices and configuration audit events.
- GlobalVatSettingTests verifies VAT propagation and preservation of existing booking tax rates.
- Browser regression: tests/browser/rental-defaults-browser.cjs exercises all seven fields, persistence on reload, independent unsaved fields, validation, empty values and retention pagination. API responses are mocked; live settings are never changed.

Run the browser test against a Vite dev server using CREMS_WEB_URL (defaults to http://localhost:5173) and PLAYWRIGHT_MODULE when Playwright is installed outside the project.

## Reverification — 19 October 2026

The local API on port 5080 was still an older Debug process: the retention-review route returned 404. Restarted it with the updated Release build; the protected route now returns 401 without authentication, as expected. Refresh the settings screen to load the current server descriptions.

- 91 relevant backend regression tests passed (settings validation and runtime behavior, VAT, return charges/bond, and customer journeys).
- RentalSettingsSqlIntegrationTests passed against a uniquely named temporary SQL database using real HTTP endpoints and cookie authorization. Verified all seven values persist, prefixes normalize, invalid updates fail, unauthorized access is rejected, VAT preserves an existing booking, lockout options use saved values, audit records are written and retention review uses the saved age.
- Browser regression passed again with isolated mocked API data, including all seven controls, reload persistence, preservation of unsaved fields, validation and retention pagination.
- The temporary SQL database was deleted after the run. Tests did not modify the existing CREMS database or its configured values.

To run the opt-in SQL test, set CREMS_RUN_SETTINGS_SQL_TESTS=1 and provide CREMS_TEST_SQL_CONNECTION (or a DefaultConnection user secret). The test replaces the database name with a generated CremsSettingsTest_ name and checks that name before cleanup. Do not print connection strings in test reports.
