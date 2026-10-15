# CREMS management reporting definitions

CREMS uses Fiji calendar dates (`Pacific/Fiji`). Date filters are inclusive of the selected start and end dates. The backend converts them to a half-open timestamp interval `[start, following day)` before querying records. These definitions are implemented in `ReportingKpis` and returned alongside management reports.

| Measure | Definition | Important boundary |
| --- | --- | --- |
| Revenue | Pre-tax subtotal of issued, non-voided invoices in the period, less applied credit-note subtotal linked to those invoices. Drafts are excluded. | Not payments received or scheduled booking value. Credits are reflected at current state, not reconstructed as-of period end. |
| Gross invoiced | Tax-inclusive total of the same invoices. | This is billed value, not revenue excluding tax. |
| Outstanding | Current positive balance of non-voided invoices issued in the period. | It is **not** a reconstructed balance at the period end. |
| Maintenance cost | Actual cost of non-cancelled jobs reported in the period; if actual is absent, recorded cost components. | Estimates alone are excluded. |
| Operating contribution | Allocated invoiced revenue less maintenance, direct asset costs, booking charge costs and personnel timesheet costs. | Excludes overhead, depreciation, finance charges and tax; it is not audited net profit. |
| Margin | Operating contribution divided by allocated pre-tax revenue, multiplied by 100. | Zero revenue produces a displayed 0% margin. |
| Hired days | Union of on-hire intervals for completed or active rentals intersecting the period. | Overlapping bookings count once. |
| Available days | Capacity days since asset acquisition within the period, less the union of hired and recorded maintenance downtime. | Capacity is a management estimate; historical retirements and unrecorded outages are not reconstructed. |
| Utilization | Hired days divided by capacity days, multiplied by 100. | Current status is a separate snapshot, not historical utilization. |
| Downtime | Union of maintenance intervals from start/report to release/completion, clipped to period. | Open jobs stop at now; overlapping jobs count once. |

An invoice or booking that contains several assets has no asset-level invoice-line allocation in the current data model. The period P&L apportions the invoice's pre-tax subtotal using each asset's booked daily-rate value, then assigns the exact rounding remainder to the last asset. This is an estimate and should be labelled as such. If an invoice has no asset-bearing booking item, its revenue remains unallocated rather than being silently assigned.

Exception reports are operational worklists. The unusual-discount threshold defaults to 15% of the booking's scheduled item value. Exceptions link to their source workflows; they are not accounting adjustments.
