# Configurable Organization & settings

Implemented on `configurable-system-settings-enhancements` in `C:\Users\jaysh\Documents\CREMS`.

## Staff experience

Organization & settings retains its existing structure and features:

- Business setup: divisions and services, branches, branch operations, asset categories.
- Booking & pricing: division booking policies, fees and charges, approval rules, rental defaults and retention review.
- Communications: customer notification templates.

System health & audit is a separate page at `/staff/system-health`, placed last under Administration. Its existing health and administrator activity functions are reused.

Add business / division and Configure division open the five-step guided setup:

1. Division details, capabilities, services, editable starter templates and visibility.
2. Existing division booking policy, service quotation routes, required documents, delivery requirements, operating branches and scoped customer questions.
3. Calendar and usage-based preventive maintenance intervals.
4. Category asset types, custom asset fields and separate pre-hire / post-hire inspection templates.
5. Review, existing division financial defaults and terms, and save.

Templates cover vehicles, heavy machinery, generators, forklifts, scaffolding, waste bins, portable toilets, property spaces and ICT / engineering equipment. They create configuration, not fictional operational assets. Category default asset types prevent a new equipment category from inheriting the asset register's vehicle default.

## Persistence and propagation

| Configuration | Stored in | Used by |
| --- | --- | --- |
| Division policy and scoped customer questions | `Division.CustomerBookingConfigurationJson` / `DivisionBookingFlow` | Public checkout, manual quotation conversion and booking workspace |
| Operating branches | Existing `BranchDivision` and `BranchDivisionService` records | Existing asset classification, public availability and scoped staff configuration |
| Customer responses and inspection requirements | `Booking.ConfigurationSnapshotJson` | Staff booking review and pickup / return workflows |
| Asset fields | Existing `AssetAttributeDefinition` and `AssetAttributeValue` records | Asset create/edit and existing customer-visible specifications |
| Category asset type | `AssetCategory.DefaultAssetType` | Default selection in asset registration |
| Handover and return checklists | Existing `InspectionTemplate` records | Existing pass/fail inspection workflows, signatures and photographic evidence |
| Inspection readings | Recognized flags in existing service inspection settings | Staff forms and server validation |
| Preventive intervals | Existing service `MaintenanceRulesJson` | Existing maintenance plans and background job generation |
| Branch hours and closures | Existing operating periods and calendar exceptions | Branch configuration views and existing booking confirmation rules |

The setup endpoint commits the complete configuration in one transaction. A fingerprint of the current configuration rejects edits opened before another administrator changed its underlying settings. Unknown existing division policy properties are preserved. Existing financial, branding, approval, service and category values are loaded for editing rather than replaced with new defaults.

Branch hours, closures and services also save together. Branch editing uses a revision check and cancels obsolete reads when switching branches. Historical calendar exceptions are retained. Staff do not receive one branch's stale settings while another branch is loading.

Customer questions can apply to the whole division, a service, or an asset category within that service. Text, long text, number, integer, date, choice and yes/no answers are validated on the server. A negative yes/no answer is a valid answer. Manual quotation conversion collects the same configured questions. Reassignment to a different service/category requires its applicable answers and refreshes the inspection requirements.

New bookings keep their question labels, answers, meter/fuel requirements and effective category templates. Editing a template later does not rewrite those bookings. Historical bookings without a snapshot keep legacy optional-reading behavior unless an explicit service rule applies; their existing category templates remain available.

Pre-hire preparation may record failed checks. Asset release still requires the configured checks to pass. Returns require every configured check to have a result and retain the existing fault, damage, maintenance, bond and invoice behavior. Meter and fuel requirements are independent.

Fields containing asset values cannot be removed or retyped through either the wizard or the existing category editor. Values are saved with the asset in the same save operation. Existing IDs remain stable during configuration edits.

## Access

Configuration writes retain the existing Super Administrator policy. Other staff can read their operating configuration without receiving configuration-write access. Branch managers, rental officers and maintenance officers are filtered by both authorized division and branch. Administrator retains its existing group-wide role behavior; this change does not introduce a new role or expand staff access to operational records.

The read-only operating view refreshes when revisited, when its window regains focus, or when configuration changes in the same app window. Customer asset details are fetched when preparing checkout. Existing booking, pricing, availability, licence, approval and maintenance-release checks remain in their existing paths.

## Database changes

Two additive EF migrations are included:

- `20261022012927_ConfigurableOrganizationSetup`: booking configuration snapshots.
- `20261022014124_ConfigureAssetCategoryType`: optional category default asset type.

Existing bookings and categories remain valid without backfilling or recreating records. The migrations are applied through the project's normal migration/startup process.

## Verification

- API regression suite, including setup persistence, stale edits, protected fields, scoped staff reads, customer questions, manual quotation conversion, inspection snapshots and calendar maintenance.
- Disposable SQL/HTTP test with real cookie authorization, migration application, setup and asset persistence, atomic branch saves, stale revisions and configured inspection enforcement. It creates and deletes only a uniquely named `CremsOrganizationTest_...` database.
- Production TypeScript/Vite build and frontend unit tests.
- Browser checks for guided setup, persisted-form reload, conflict feedback, retained settings sections, all existing staff role views and phone layout.
- Customer browser regression tests, including required custom questions in booking and quotation payloads.
- Existing return-charge and property monthly-pricing browser checks.

Run the focused SQL test with `CREMS_RUN_ORGANIZATION_SQL_TESTS=1` and a securely configured `CREMS_TEST_SQL_CONNECTION`, or the existing API user-secret connection. No email delivery workers run in that test host.

Run `tests/browser/configurable-organization-browser.cjs` against Vite with `CREMS_TEST_URL` and, if needed, `PLAYWRIGHT_MODULE`. These browser fixtures intercept API writes; the separate SQL/HTTP suite verifies persistence and permissions.

This extends the existing asset-backed hire system. Configuring a merchant does not introduce a banking, retail point-of-sale, or standalone financial-services module.
