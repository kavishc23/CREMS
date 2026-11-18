# Repository handover review — 18 November 2026

## Release status

Reviewed the fetched `origin/main` baseline `cc7f2e2`. This cleanup preserves the application, database migrations, dependency lock, tests, startup scripts, catalogue utilities, catalogue images and maintained operating guides. It does **not** establish production readiness. CREMS is locally developed; Carpenters will deploy and accept it on their own server.

## Cleanup

- Removed one-off `ManualTraining`, `PrepareClientHandover` and `SeedRentalPractice` utilities, including fictional signatures and event-approval fixtures.
- Removed the committed Python bytecode cache; added ignore rules for Python caches, coverage and test results.
- Kept test fixtures used by maintained tests and development seed support required by existing code. Production startup does not run the Development seed path.
- Kept asset photographs: deleting them would break existing catalogue references. New runtime uploads and customer documents remain ignored.
- Kept maintained Markdown guides; these explain configuration, maintenance, reports, installation and recovery. No blanket Markdown deletion was performed.
- Local `.env`, databases, uploads, authoring outputs and ignored working files are not part of the source handover. Transfer required database/files separately under the operations procedure. Do not give the client a ZIP of the entire local working directory.

## Verification and unresolved items

| Check | Result |
| --- | --- |
| Backend default suite | 673 passed, 0 failed, 5 opt-in SQL tests skipped |
| Isolated SQL integration rerun (all three opt-in flags enabled) | 5 passed, 0 failed, 0 skipped |
| Frontend suite | 672 passed, 66 failed, 3 unhandled errors; 60 files passed and 16 failed |
| Frontend production build | Passed; large bundle warning remains |
| Conflict-marker and diff whitespace checks | Passed |
| Targeted tracked credential pattern scan | No matches for private-key blocks and common GitHub/AWS/Brevo token patterns; this is not a full historical secret audit |

NuGet restore reported NU1900 because the vulnerability feed could not be reached; backend test success is not a dependency security clearance.

The frontend results are a handover verification gap. Examples include `RentalsWorkflow.test.tsx` rendering without AuthProvider, Node 25 localStorage behavior, jsdom lacking scrollIntoView, and selectors/assertions that no longer match the current workflow. They must be reconciled with current behavior and rerun on a supported Node LTS version. A successful build does not supersede these failures. Do not remove failing tests or weaken live validation to obtain a passing report.

The host used Node 25.8.1, outside jsdom 31's declared range. Use Node 22.22.2+ or 24.15+ LTS for repeatable frontend verification. The compatible lockfile update refreshed TypeScript ESLint, minimatch and Vite dependencies. The subsequent **online** npm audit still reported 12 high-severity entries, all in development dependencies, propagated from picomatch ReDoS (GHSA-fc5q-39xc-vjpf). The registry's latest picomatch was 4.0.7, still within the advisory range. Do not treat an offline audit's zero as security clearance. Review the advisory and a compatible fix before exposing development/build tooling to untrusted inputs; do not apply forced major downgrades without validation.

Advisory: https://github.com/advisories/GHSA-fc5q-39xc-vjpf

## Branch review

Counts below are relative to fetched main before this cleanup: behind = commits only on main; ahead = commits only on that branch. Branches were preserved; no remote history was rewritten or deleted. Older branch tips are historical development work, not separately certified release builds.

| Remote branch | Behind main | Ahead of main |
| --- | ---: | ---: |
| `origin/Images` | 185 | 2 |
| `origin/Kavish-Branch` | 2 | 0 |
| `origin/Minor-tweaks-in-customer-page` | 190 | 1 |
| `origin/OCR-license-validation-implementation` | 116 | 0 |
| `origin/configurable-system-settings-enhancements` | 66 | 0 |
| `origin/customer-booking-logic-bug-fix` | 147 | 0 |
| `origin/customer-functionality-fixes` | 165 | 0 |
| `origin/customer-page-changes` | 187 | 1 |
| `origin/customer-page-updates-2` | 168 | 0 |
| `origin/customer-password-toggle` | 180 | 0 |
| `origin/feat/notification-recipients-email` | 115 | 0 |
| `origin/feat/staff-inspection-history` | 112 | 0 |
| `origin/feature/automatic-bond-overdue-charges` | 126 | 0 |
| `origin/feature/maintenance-technician-locations` | 91 | 0 |
| `origin/final-admin-ui-testing` | 27 | 0 |
| `origin/final-customer-website-ui-testing` | 11 | 0 |
| `origin/fix/asset-meter-validation-and-security-dependencies` | 58 | 0 |
| `origin/fix/asset-transfer-controls` | 38 | 0 |
| `origin/fix/customer-notification-announcements` | 122 | 0 |
| `origin/fix/hire-inspections-checkout-customer-images` | 154 | 0 |
| `origin/fix/inspection-bulk-actions-qr-scanner` | 84 | 0 |
| `origin/fix/maintenance-tax-and-integration-audit` | 92 | 0 |
| `origin/fix/maintenance-workflow-integrity` | 101 | 0 |
| `origin/fix/organization&settings-systemhealth&audit` | 69 | 0 |
| `origin/fix/public-division-catalogue-sessions` | 128 | 0 |
| `origin/fix/systemconfiguration-rentaldefaults-rates&charges` | 83 | 0 |
| `origin/main` | 0 | 0 |
| `origin/maintenance-page-usability` | 100 | 0 |
| `origin/manage-booking` | 179 | 0 |
| `origin/manage-bookings` | 181 | 0 |
| `origin/minor-changes` | 128 | 0 |
| `origin/ocr-scanning-refinements` | 73 | 0 |
| `origin/reporting` | 86 | 0 |
| `origin/stage-approval-rule-refinement` | 132 | 0 |
| `origin/user-ui-update` | 180 | 0 |

`customer-page-changes` has a commit whose patch is already present in main (`git cherry` marks it equivalent). `Images` and `Minor-tweaks-in-customer-page` contain legacy unique commits. Their image/development-seed and old customer-screen changes were reviewed, but not blindly merged: main already has later catalogue/photo handling and a redesigned customer administration workflow, and the Images branch also contains old assignment/output files and RefreshOnStartup=true. Preserve those branches for history. The canonical handover source is main plus this cleanup, not a merge of every historical branch.

## Before live use

Complete Carpenters server deployment, production configuration, licensing decisions, credential ownership, a coordinated database/upload/key backup and isolated recovery test. Approve a clean opening dataset. Run server-specific acceptance checks, resolve the frontend verification gap and record release tag/hash and outstanding issues with the client.
