# Initial architecture

CREMS begins as a modular monolith: one React frontend, one ASP.NET Core API, and one SQL Server database. This keeps deployment and debugging manageable for a four-person team while retaining a clean API boundary for future clients.

## Backend modules

- Identity and access
- Branches
- Customers
- Assets
- Bookings and availability
- Rentals and returns
- Inspections and damage
- Maintenance
- Reporting
- Notifications
- Auditing

Modules initially share one database and process. Business rules belong in backend application/domain services, never solely in React components or controllers.

## Key design decisions

- GUID primary keys avoid exposing predictable record counts and simplify offline imports.
- `DateTimeOffset` is used for events; Fiji-local conversion occurs at system boundaries.
- Assets have explicit states rather than deriving operational state from UI labels.
- Booking lines carry their agreed daily rate so later master-rate changes do not alter history.
- SQL constraints and transactional availability checks will jointly prevent double booking.
- Transactional records will be deactivated or cancelled rather than physically deleted.

## Authentication

ASP.NET Core Identity is configured with the roles `Administrator`, `RentalOfficer`, and `BranchManager`. The first production administrator must be provisioned through a controlled deployment process; public registration will not remain enabled for the deployed staff system.

