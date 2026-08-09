# Requirements register

This document records the decisions that must be confirmed before implementing business workflows. Replace `Pending` only after a client-approved answer is obtained.

| ID | Decision | Status | Client-approved answer |
|---|---|---|---|
| R-001 | Which branches are included in the MVP? | Pending | |
| R-002 | Can users view and reserve assets belonging to other branches? | Pending | |
| R-003 | What can Administrator, Rental Officer, and Branch Manager roles do? | Pending | |
| R-004 | Can a booking contain multiple assets? | Pending | |
| R-005 | What time buffer is required between return and the next rental? | Pending | |
| R-006 | How are daily, weekly, late, fuel, damage, and extension charges calculated? | Pending | |
| R-007 | Does CREMS generate invoices or only record accounting-system references? | Pending | |
| R-008 | What makes an asset unavailable, and who may override that state? | Pending | |
| R-009 | Which inspection checklist applies to each asset category? | Pending | |
| R-010 | Are inspection photographs and customer signatures mandatory? | Pending | |
| R-011 | Is maintenance triggered by date, mileage, operating hours, or all three? | Pending | |
| R-012 | Which dashboard measures and reports are mandatory for MVP acceptance? | Pending | |
| R-013 | Which email events, recipients, and reminder schedules are required? | Pending | |
| R-014 | Is QR/barcode scanning required in the MVP? | Pending | |
| R-015 | Where will the system, SQL Server, and uploaded documents be hosted? | Pending | |
| R-016 | Who is the authorized client product owner and scope approver? | Pending | |
| R-017 | What anonymized forms, agreements, reports, and sample data can be supplied? | Pending | |
| R-018 | What measurable scenarios define user acceptance? | Pending | |

## Proposed MVP boundary

Included unless the client changes priority:

1. Staff authentication and three roles
2. Branch, customer, vehicle, and equipment records
3. Booking with conflict detection
4. Rental handover, extension, return, and closure
5. Pre-rental and post-rental inspections
6. Damage and maintenance request logging
7. Essential dashboard and reports
8. Audit history for critical changes

Proposed later phase:

- Advanced revenue analytics and Metabase
- Automated email workflows
- QR/barcode scanning
- Depreciation calculations
- Complex inter-branch transfer approvals
- Nonessential exports and report customization
# Client meeting update — group-wide modular CREMS

The following requirements were confirmed during the first client meeting and now guide the implementation:

- CREMS must support multiple Carpenters Fiji divisions through reusable modules rather than separate hard-coded systems.
- The initial operational focus remains rental revenue and asset-maintenance expense management.
- A division may enable only the capabilities it needs, such as rental, maintenance, property leasing, logistics, retail or personnel-supported hire.
- Individual and corporate customers must eventually use authenticated customer accounts across participating divisions.
- The public experience must use progressive disclosure: ordinary vehicle-rental customers should not be overwhelmed by unrelated shipping, equipment or property workflows.
- Some hired assets, including generators or excavators, may require or optionally include trained personnel.
- Property rental/leasing must be representable as another division capability.
- Carptrac equipment hire and Carpenters Hardware tool hire remain unconfirmed; CREMS must support quote-only/configurable offerings without presenting them as confirmed online rentals.

Implementation status (6 August 2026): division and service-offering configuration, capability flags, personnel requirements, asset-to-division links, public division discovery and administrator configuration have been added. Individual and corporate customer registration, customer-only sessions, account-linked bookings and a cross-division customer booking view are also implemented. Customer email verification uses expiring one-time codes. Staff can now invite a pre-existing customer record to online access without duplicating or exposing its history; the customer activates it with a 24-hour code and chooses a password. Detailed division-specific workflows remain discovery/implementation items.

Division-aware staff scope is now implemented. A non-administrator staff account requires both a division and branch assignment. Dashboard totals, assets, bookings, rental lifecycle, agreements, QR check-in/check-out and maintenance are constrained by both values. Branches can participate in multiple divisions, and administrators maintain those memberships from the Branches page. Staff can view the directory of branches enabled for their division while operational transactions remain limited to their assigned branch.
