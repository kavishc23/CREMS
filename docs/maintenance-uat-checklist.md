# Maintenance staff acceptance checklist

Working project: `C:\Users\jaysh\Documents\CREMS`, branch `maintenance-page-usability`.

Status: prepared for staff UAT; no staff acceptance or client sign-off recorded. Planned UAT window: 19–23 October 2026; handover: 26–30 October 2026.

Run with approved branch roles, representative vehicles and equipment, real category checklists and agreed service intervals in a test environment. Record the asset/job reference and actual outcome for every case. Do not use an operational hire to perform these checks.

| Case | Staff role | Expected result | Actual result / evidence / tester / date |
| --- | --- | --- | --- |
| Twelve active jobs | Rental officer | All 12 entries show together using the page scroll; no inner vertical table scrollbar; search and branch filters work. | Pending |
| Overdue service vs due today | Branch manager | Yesterday's date is overdue; today's date is due now. Reached meter equals due now; exceeded meter is overdue. Summary buttons open matching queues for the selected branch. | Pending |
| Repair deadline | Branch manager | Missed expected release is flagged until release; closed work awaiting safety remains flagged. Cancelled/released work is excluded. This is separate from service due. | Pending |
| Initial configured meter interval | Administrator / manager | Rules view shows configured interval and initial target; count and schedule agree at below/equal/above threshold. Invalid rules show a warning. | Pending |
| Completed service and recurrence | Maintenance staff | Completed next meter target takes precedence; monthly recurrence calculates the next date if no explicit date is entered. Explicit dates take precedence. | Pending |
| Hired asset and duplicate prevention | Maintenance staff | Due hired assets are visible for review; automatic jobs wait for an eligible status; existing open work prevents a duplicate job. | Pending |
| Inspection → repair | Rental officer | Log a failed inspection/damage report, trace its maintenance source, open the inspection record and verify author, notes, meter and checks. | Pending |
| Assignment and status | Maintenance staff | Search eligible technicians/suppliers; start work, wait with reason, cancel/reopen with reason; inspect corresponding audit records. | Pending |
| Inventory and expenditure | Maintenance / finance staff | Issue parts, return unused stock with a reason, retain original valuation, record labour hours/rate, fuel, contractor costs and invoices. Totals agree with the stock ledger. | Pending |
| Cumulative history cost | Finance staff | Recorded cost includes actual costs on active/cancelled/completed work, completed cost remains distinct, and estimates do not inflate actual totals. | Pending |
| Work completion | Maintenance supervisor | Missing work summary/meter blocks completion. Successful completion leaves the asset unavailable in Inspection. | Pending |
| Safety release blockers | Inspector / supervisor | Open repairs, active/overdue hire, current allocation, transfers, newer failed checks, due service targets and decreasing meter readings block release. Failed checks are resolved through further work. | Pending |
| Repair → safety → booking → profitability | Inspector / rental / finance staff | A passed authorized safety check records the inspector and makes the asset available; a subsequent booking succeeds; actual costs appear correctly in profitability. | Pending |
| Related job and history navigation | Maintenance staff | Open a previous service/failure record and inspection/audit dialogs; upcoming targets are visible; unsaved navigation asks whether to discard changes. | Pending |
| Role and branch boundaries | Administrator / restricted staff | Other branches/divisions cannot be read or changed; costs and invoice evidence are absent without financial access; complete/release controls match permissions. | Pending |
| Conflict and network recovery | Maintenance staff | Concurrent stock/job edits reject stale saves, preserve input and permit retry after review; network failure does not discard entered details. | Pending |
| Phone and tablet | Branch staff | Cards/actions fit, rules and record dialogs are usable, and opening controls causes no horizontal page jump. | Pending |

Record defects with severity, reproduction steps and job/asset reference. Retest each resolved defect. Attach screenshots or exported test evidence without customer personal data.

Acceptance record:

- Branch representative / date: Pending
- Maintenance supervisor / date: Pending
- Rental officer / date: Pending
- Finance representative / date: Pending
- Administrator / date: Pending
- Client acceptance / date: Pending
- Outstanding defects and release decision: Pending

Handover requires the staff guide, approved role settings and inspection templates, confirmed service intervals, legacy record review, and a tested database backup/restore runbook. Automated test results support this checklist; they do not fill its staff acceptance fields.
