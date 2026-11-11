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
