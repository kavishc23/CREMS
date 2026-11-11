# Asset usage and rental performance

Availability now includes an **Asset category** filter. Category, branch, division,
search and available-only filters intersect. Changing a category, branch or division
clears an incompatible individual asset selection. The filter controls wrap into
rows on smaller screens instead of overlapping or requiring horizontal scrolling.

Open an asset's **Full profile → Overview** to see its time summary. Choose the
last **30, 90 or 365 days** above the tabs. **Detailed asset report** uses the same
period and includes the summary in its preview, CSV and PDF. Financial permissions
are not required to view operational time.

## Meaning of the figures

- **Rented:** elapsed pickup-to-return time, including overdue hires until return.
  Historical completed hires with no pickup use booked dates and are marked as estimates.
- **Maintenance:** elapsed time from reporting a job to its safety release. Finishing
  repairs alone does not make the asset ready for another hire.
- **Available:** time without a recorded hire, reservation, maintenance or other hold.
- **In service:** rented plus available time. Reserved time is shown separately.
- **Other unavailable:** recorded inspection, transfer and out-of-service holds.
- **Utilisation:** rented hours divided by the observed period's hours.
- **Downtime:** maintenance hours divided by the observed period's hours.

These are calendar hours, **not engine-running hours**. Days are hours divided by
24. Branch opening hours and turnaround buffers are not deducted. Missing historical
holds can overstate available time, so the summary explains its recording basis.
Overlapping records count once. Maintenance takes priority over rentals and overlap
warnings identify records needing review. Calculations read all history, independently
of the latest-50-record lists in the profile. Imported history can predate creation in CREMS.

## Demonstration asset

Search **DEMO-UTIL-001**, named **DEMO - Rental performance vehicle**.
The local database contains a dedicated fictional vehicle copied from an existing
configured vehicle category. Existing client assets and their histories remain unchanged.

The scenario includes eight completed hires across six months, two upcoming confirmed
bookings, eight released maintenance jobs, pickup/return and safety inspections,
meter readings, invoices and fictional receipts. Every scenario record uses DEMO references.
Notifications are suppressed. No real payment or workshop activity occurred.

At creation on **11 November 2026 (Fiji)**, the last 90 days show:

| Measure | Hours | 24-hour days |
| --- | ---: | ---: |
| Rented | 744 | 31 |
| Available | 1,140 | 47.5 |
| In service (rented + available) | 1,884 | 78.5 |
| Maintenance | 276 | 11.5 |
| Total observation period | 2,160 | 90 |

Utilisation is **34.44%** and maintenance downtime is **12.78%**. Six historic
hires and six maintenance jobs fall within that window. The figures change as the
rolling period advances. Try 365 days to include the older history.

Business demonstration: open the profile, compare rental use with downtime, then
generate the detailed report. Discuss idle availability alongside downtime rather
than declaring an asset suitable based on a single percentage. Use the Financials
tab, where permitted, to compare revenue and maintenance expenditure as well.

To add the scenario to another local development database:

```sh
./.dotnet/dotnet run --project ops/SeedAssetUsageDemo -- --apply
```

The importer only allows the local `Crems` SQL database and adds one dedicated
asset. Repeating it does not duplicate records. To verify persisted totals immediately
after seeding, use `--verify` in place of `--apply`.

The backend must run the updated build for category fields and usage summaries to
appear. No schema migration is required.
