# Using maintenance in CREMS

Use **Asset operations → Maintenance work**. The module supports the FSD maintenance workflow F-MNT-01 through F-MNT-06. The supplied FSD and plan are reference documents; client acceptance remains a separate UAT activity.

## Daily work

1. Review **Unassigned jobs**, **In progress**, **Waiting for parts**, and **Awaiting safety check**. Select a summary to open the relevant queue. Use branch, technician, priority, status and search to narrow it. Filters stay in this browser tab's session; Clear filters resets them. Lists use server search and pages of 25, 50 or 100.
2. Select **Review due assets** for service dates and meter targets. Date and meter limits use whichever comes first. The automatic worker checks every 15 minutes; recording a meter can also generate a configured meter service. An existing open job prevents duplicates. Closed work awaiting verification still blocks availability.
3. Select **Log maintenance job**, search or paste an asset number from its QR label, and explicitly choose the correct asset and branch. Describe the fault, service type, priority and expected release. Inspection/damage sources require the originating inspection or an external reference. Your account and report time are recorded.
4. Assign an active technician from the asset's branch and division, or an external supplier. Technicians on leave, training or unavailable cannot be newly assigned. Existing assignments without a master link stay visible until deliberately changed.
5. **Start work**, **Wait for parts**, **Cancel job**, and **Complete work** are controlled status actions. Waiting, cancellation and reopening require a reason. Cancelled work remains in history and does not certify asset safety. Reopen cancelled work before completing it.

## Work, expenses and evidence

Use **Work and service targets** to describe the repair and tests, record the final meter and downtime, and set the next service date/meter. Leave downtime blank to calculate elapsed hours at completion; entering zero records zero. A monthly interval calculates a future service date when no explicit date is entered and carries forward into generated preventive work.

Save changes before **Manage parts and stock movements**. Stock issues and returns retain quantities and original issue unit costs. Return unused stock with a reason. Job parts cost cannot fall below net inventory issued. Stock movements update the job version; stale edits cannot overwrite them.

Staff with financial permission can use **Expenses** for an estimate, parts, labour hours/rate, contractor work, transport, fuel, tax and other costs, supplier invoice and warranty details. Blank estimate means not estimated; zero means a confirmed zero estimate. Labour hours and rate must both be entered or both left blank. The detailed total is calculated; invoice-only total is also supported, but stock costs must be reconciled. The chosen entry mode survives saves, including zero totals.

Save the job before uploading **Evidence**. Attach readable PDF, JPEG or PNG files up to 5 MB, categorized as fault photos, completion evidence or supplier invoices. Invoice files and financial fields are omitted from maintenance responses for staff without financial access. Operational photos and notes should describe repairs rather than duplicate confidential invoice amounts.

## Returning an asset to service

**Complete work** requires a repair summary and final meter where the asset has a meter unit. The asset goes to Inspection and remains unavailable to bookings. Completion requires maintenance-completion permission.

An authorized inspector uses **Safety check** after work is completed or cancelled. Confirm every checklist item, enter the verified meter and test notes, then choose **Confirm safety and return to service**. The category's configured Maintenance inspection template supplies the checks; if none is usable, asset-type checks are provided. Review and approve category templates before operational rollout.

Release requires both maintenance-completion and asset-inspection permission. It is refused while another repair, active/overdue hire, current confirmed allocation, transfer, newer failed inspection, due service date or reached service meter threshold remains. Meter readings cannot decrease. Resolve failed checks through further work; do not mark an unsuccessful check as passed. A passed inspection records the inspector, checklist, meter, release time and audit trail. All closed, unreleased jobs on the same asset share that verification.

## History and recovery

**Service history** includes completed, cancelled and active records. Open an asset's workspace for service, inspection, meter and audit timelines, repeat-failure links, downtime, and authorized financial totals against book value. Totals cover all completed work; recent timelines are bounded to 50 jobs and 20 inspections/meter readings.

If another staff member changes the job, CREMS rejects the stale save and keeps your entered details. Review the changes and reopen/refresh the record before resubmitting. Network errors retain input and filters. Closing an edited workspace asks whether to discard changes; browser navigation also warns while changes remain unsaved.

## Release handover

The working project is `C:\Users\jaysh\Documents\CREMS`, on branch `maintenance-page-usability`. Database migrations add maintenance provenance, assignment, release, labour/fuel, monthly interval, estimate-presence and expense-mode fields. Existing closed jobs on already operational assets are marked **Legacy release**, without inventing a passed safety inspection. Closed jobs on unavailable assets need verification. Existing positive estimates are retained; historical zero estimates are unknown until staff confirm them.

Run normal development startup from the working project after stopping the previous API instance:

```powershell
dotnet run --project src/CREMS.Api
```

Startup applies migrations. Refresh the frontend at its existing address; Vite loads source changes. For a fresh frontend process, run `npm run dev` inside `src/crems-web`.

Before production rollout, use the 19–23 October UAT window to validate real role assignments, category safety templates, service intervals, legacy records, stock valuation, FJD tax/invoice handling, damage → repair → safety release → booking, and profitability with branch staff. The 26–30 October handover should include this guide and a database backup/restore runbook. Automated tests are development evidence, not client sign-off.


## Overdue work, service rules and record navigation

The six summary actions respect the selected branch and reset other filters when opening their queue. **Overdue service** means a service date before today in Fiji or a meter reading beyond its target. **Review due assets** also includes due-today dates and exactly reached meter targets. **Missed repair deadlines** means an expected release time has passed while the work remains unreleased, including completed work waiting for safety verification; cancelled and released jobs are excluded. A repair deadline is an estimate for rental planning, separate from the preventive service target.

In **Preventive schedule**, select **View service rules** to see the next date, current meter, next threshold, configured meter interval, target source, remaining distance/hours and recorded monthly recurrence. A completed next-meter target takes precedence. Without that target, the initial configured interval starts at zero or the interval follows a recorded completed preventive service meter. Unsupported/invalid service rules show a warning; they are not silently treated as automatic triggers. Hired/transfer-status assets remain visible for review but automatic work waits for an eligible asset status.

**Asset history** now starts with the upcoming service overview. Select another job or related failure to open its workspace; unsaved changes require confirmation before navigation. Select **View originating inspection** or an inspection-history entry for its author, meter, notes and named checklist results. Select an audit entry to open its recorded action, author, time and reference. Financial summaries remain restricted by role.

Total recorded cost includes actual costs on active, cancelled and completed jobs. Completed cost and downtime remain separate. Estimates do not count as actual costs. Warranty inputs are revealed with **Add warranty details** or when an existing warranty is recorded.

Use [maintenance-uat-checklist.md](maintenance-uat-checklist.md) for staff acceptance, evidence, defects and sign-off. Its Pending entries must be completed by the named staff roles; development checks do not constitute their approval.
