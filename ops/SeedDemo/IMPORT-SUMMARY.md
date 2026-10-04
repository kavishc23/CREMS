# Demo import completed

Database: local `Crems` SQL Server.
Committed: 2026-09-27 22:10 UTC (2026-09-28 in Fiji).
Marker: `demo.realistic-september-v1`.

**2,926 records added across 43 record types.** The transaction committed successfully. A subsequent read of SQL Server confirmed the import marker and the asset, customer, booking, maintenance and invoice counts below.

| Area | Added |
| --- | ---: |
| Assets / customers | 48 / 48 |
| Staff and customer user accounts | 36 |
| Bookings / booking items | 168 / 168 |
| Rental agreements / addendums | 156 / 8 |
| Invoices / lines / payments / allocations | 144 each |
| Draft credit notes | 6 |
| Asset inspections / rental inspections | 288 / 300 |
| Asset lifecycle events / meter readings | 348 / 144 |
| Maintenance jobs / part usages | 66 / 48 |
| Maintenance reference attachments | 12 |
| Suppliers / inventory parts / purchase orders | 8 / 24 / 4 |
| Personnel / qualifications | 12 / 12 |
| Personnel assignments / timesheets | 8 / 8 |
| Quotes / quote revisions | 48 / 48 |
| Corporate accounts / support cases | 16 / 24 |
| Dispatch jobs / proposed transfers | 24 / 6 |
| Approval requests / stage decisions | 12 / 12 |
| Management tasks / business alerts / in-app notices | 26 / 12 / 12 |
| Asset costs / asset-specific pricing rules / telematics snapshots | 48 each |
| Rental incidents | 8 |
| User role assignments | 36 |
| Import audit entry / import marker | 1 / 1 |

Existing records and organizational configuration were preserved. All new customer, supplier and account email addresses are fictional `example.invalid` addresses. No outbound email or SMS was queued by the importer. No tests were run. The importer was compiled and executed to perform the requested database write.

Refresh the frontend to load the new records. Demo accounts need an administrator password reset before sign-in. See README.md for the repeat-safe import command.
