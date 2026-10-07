# Maintenance implementation review

Working project: `C:\Users\jaysh\Documents\CREMS`.

Branch: `maintenance-page-usability`.

Reviewed against the supplied screenshots, CREMS description, FSD v1.0, Project Plan and milestone image. These documents are requirements references, not additional instructions to execute. The FSD is a client-validation draft; client sign-off is not assumed. Maintenance is scheduled for 5–9 October, integration/UAT for 19–23 October and handover for 26–30 October.

## Implemented

| Recommendation / FSD | Result |
| --- | --- |
| Clear daily workflow | Six actionable summaries separating overdue service and repair deadlines; Active work, Preventive schedule, Safety checks and Service history views; next-action buttons. |
| F-MNT-01 intake | Explicit searchable asset selection; branch/division technician master assignment; supplier; priority; reporter; source inspection/damage reference; expected release; repeat-failure links. |
| F-MNT-02 preventive service | Date/km/engine-hour thresholds, whichever first; existing automatic generation retained; deduplication; monthly interval calculation and carry-forward; due count links to the schedule. |
| F-MNT-03 detailed expenditure | Parts ledger and original-cost returns; labour hours/rate; transport/fuel/contractor/tax/other costs; FJD totals; supplier invoice/warranty; missing versus zero estimate; persistent detailed versus invoice-only entry mode. |
| F-MNT-04 controlled transitions | Start/wait/cancel/complete/reopen actions; permission checks, transition reasons, version conflicts, completion notes and meter validation. |
| F-MNT-05 return to service | Completing work leaves the asset unavailable in Inspection; separate authorized passed inspection; hire/allocation/transfer/other-job/newer-failure/service-target checks; meter monotonicity and recorded inspector. Booking/public availability excludes Inspection. |
| F-MNT-06 history | Upcoming service/rules overview, linked job/inspection/audit records, service/meter timelines, repeated faults, downtime and authorized cumulative/completed-cost metrics. |
| Register usability | Server search/sort/filter/pagination at 25/50/100; session filters; tables expand with their current page of results; normal page scrolling; mobile cards; full-width mobile record workspace; explicit empty/error/success states. |
| Permission boundaries | Scoped branch/division APIs; maintenance financial fields and invoice evidence omitted without permission; finance input preserved rather than overwritten by operational staff. |
| Recovery and handover | Unsaved-change guard; stale-save protection; attachment validation; retry; updated in-app help and maintenance staff guide. |

## Shared horizontal shift fix

`StableViewport` is mounted at application entry for staff and customer routes. Supporting browsers reserve root scrollbar space and suppress duplicate MUI body/fixed-element padding compensation. Overlay scroll locking stays enabled; other browsers retain MUI's default compensation. Shared browser checks cover selects, nested selects, dialogs and temporary drawers at desktop/tablet/phone widths. The redesigned maintenance warning also wraps its action on phones to prevent document overflow.

## Verification and practical limits

The backend suite covers completion/release permissions, branch/division isolation, work summaries and meters, failure/hire/transfer/open-job blockers, stock valuation and concurrency, cost preservation, references, preventive dates and deduplication. An isolated SQL HTTP test uses real controllers, cookies, policies and queries without production startup seeding or email delivery. Browser tests mount the actual page and staff shell against fictional intercepted API data and cover intake, transitions, expenses, stock, evidence controls, safety release, conflicts, filtering, persistence, mobile geometry, permission-controlled actions and unsaved changes. They do not prove production login or external email delivery.

Legacy migration preserves already operational assets' closed jobs as explicitly marked legacy releases; it does not invent safety evidence. Operational adoption still needs staff UAT, approved category checklists, real service intervals and role settings. Production deployment and client acceptance are separate from this local implementation.

Read `maintenance-staff-guide.md` for daily operation, migration behavior, restart steps and the UAT/handover checklist. User-generated verification output directories were preserved.

Final validation on 7 October 2026: 254 backend tests passed with no skips, including the isolated real-SQL integration test; production frontend build and changed-file ESLint passed; the full maintenance browser workflow and shared viewport regression passed at 1440, 768 and 390 px. No pending EF model changes remain. The three new migrations were applied to the local development database, and the updated API was verified healthy on port 5080 with the workspace/release routes present. Codex preview processes were subsequently stopped at the user’s request; previews are now managed by the user in VS Code. Changes remain local on the requested branch; production deployment and client UAT were not performed.


## Recheck against the pasted recommendations — 7 October 2026

This recheck compares the recommendations with the current source. Automated validation is evidence of implementation, not staff acceptance or production certification.

| Recommendation | Status and evidence |
| --- | --- |
| Separate work completion and verified return to service | Implemented: completion holds the asset in Inspection; the release endpoint requires authorized checks, notes and a meter reading, and rejects operational blockers. |
| API completion validation and permission boundaries | Implemented: work summary/meter/transition validation, completion and inspection permissions, scoped data and financial-field omission in operational responses. |
| Deliberate start, wait, complete, cancel and reopen | Implemented: confirmation explanations, reasons for wait/cancel/reopen, audit records and stale-save protection. |
| Distinguish scheduled service, active preventive work and history | Implemented through schedule dates/meter targets, active-job actions and history. The schedule resolves initial configured meter intervals as well as completed targets, shows source/interval/monthly recurrence, and flags invalid or unsupported rules. |
| Job workspace and searchable assignment/source | Implemented: details, work, expenses, evidence, safety and history; technician/supplier masters; inspection/damage/preventive/manual provenance. |
| Labour, stock, external costs, invoice and downtime | Implemented with labour hours/rate, stock issue/return valuation, expense totals, evidence and downtime. History distinguishes total recorded actual cost across all jobs from completed-job cost and downtime; estimates are excluded. |
| Server search/sort/filter/pagination | Implemented at 25/50/100 rows per page with session filters. The current 12 matching jobs all fit on one page; no fixed-height inner table scrolling remains. |
| Database-backed workflow and staff UAT | Automated SQL and browser workflows were validated in the preceding implementation. Staff UAT, real operational data, approved checklists and client acceptance remain pending. |
| Short header, due warning and actionable summaries | Implemented: short header, Log action, count-based due warning and six summary actions. Overdue service has a separate count/filter; missed repair deadlines have their own count/filter and row/card/workspace indicators, including work awaiting safety release. Cancelled/released work is excluded from repair deadlines. Due-today and exactly reached meter targets are due now, rather than overdue. Summary counts respect the selected branch. |
| Asset search and clear row/action hierarchy | Implemented: explicit asset selection, asset number/name/branch/status/meter context; asset-first rows, priority/status and next action. Parts management is inside the workspace. |
| Simplified navigation and conditional forms | Implemented: active/schedule/safety/history navigation; costs and cancellation within the workspace; recurring preventive inputs conditional; warranty controls are revealed through Add warranty details or an existing warranty record. |
| Meter units, mobile cards, errors and unsaved changes | Implemented: asset-specific units, mobile cards/workspace, saved notices, retry, retained fields, version conflicts and discard confirmation. |
| Service history, repeated failures, cost, downtime and source links | Implemented: upcoming-service/rules overview, service/inspection/meter/audit timelines, related failure/service-job navigation, originating inspection and inspection-history record dialogs, audit record dialogs, repeat-failure metrics, cumulative actual cost and completed-job cost/downtime. Inspection record reads enforce asset scope; financial audit summaries stay permission-controlled. Unsaved navigation is guarded. |
| Preventive rules, recurrence and consistent due count | Implemented: recorded next date/meter/monthly intervals, recurring calculation, deduplicated automatic jobs and readable rule/target provenance. Initial configured meter triggers use the same resolver as automatic meter jobs and release validation. Unsupported configured rule keys are visibly identified as non-automatic triggers; staff must record explicit targets for them. |
| Staff guide and handover | Guide and UAT/handover checklist implemented; actual staff training, UAT and sign-off remain pending. No formal compliance claim is made. |

### Requested scrolling change

Both desktop tables now grow to fit the returned rows and use the main page scroll. The default capacity remains 25, so all 12 current matches appear together; 25/50/100 pagination remains for larger future queues. Mobile cards already use normal page flow. Record drawers and dialogs retain their own scrolling when required to keep editing controls usable.

Scrolling recheck validation: production frontend build and targeted ESLint passed. The browser workflow passed against the user's existing frontend with intercepted fictional API data; the 12-row desktop table measured 1135 px client height and 1135 px scroll height, confirming no internal vertical overflow. Mobile fit, permissions and JavaScript-error checks passed. No preview server was started.


## Remaining-item implementation

All remaining software items identified in this review have now been implemented. Staff UAT, approved operational settings, training and client acceptance require staff participation and remain pending. The executable acceptance cases and blank evidence/sign-off register are in [maintenance-uat-checklist.md](maintenance-uat-checklist.md).

No new schema migration is required for these refinements. Restart the API from the existing VS Code terminal to load the new counts, schedule resolver and scoped inspection endpoint; refresh the existing frontend. Codex did not start or stop any preview servers.

Remaining-item final validation on 7 October 2026: 263 backend tests passed with no skips, including isolated real-SQL coverage. The production frontend build and targeted ESLint passed. Browser checks passed for overdue service and deadline filters, rule display, originating inspection and audit dialogs, upcoming targets, related-record navigation with unsaved-change protection, the maintenance lifecycle, permissions, tablet/phone fit, and all 12 rows without internal vertical scrolling. Browser API data was fictional and intercepted; staff UAT remains pending. No preview server was started.
