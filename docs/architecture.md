# CREMS architecture

CREMS consists of a React frontend, an ASP.NET Core API and a Microsoft SQL Server database. The API owns business rules, authorization and persistence; the frontend presents customer and staff workflows.

## Main modules

- Identity, staff roles, access scopes and browser sessions.
- Organization, branches, divisions, services and asset categories.
- Assets, availability, utilisation and maintenance.
- Customer accounts, preferences, licence records and incident reporting.
- Quotations, bookings, pickup, agreements, returns and extensions.
- Payments, bonds, invoices and financial reporting.
- Notifications, uploaded evidence and audit history.

## Persistence and deployment

Entity Framework Core migrations under `src/CREMS.Api/Data/Migrations` define the database schema. Retain the migration history when upgrading an existing database. Event timestamps use `DateTimeOffset`; user-facing dates use Fiji time. Bookings retain agreed rates and signed agreement snapshots.

Uploaded files and data-protection keys require backup alongside SQL Server. Keep credentials and client uploads outside source control. Development catalogue images bundled in the repository support the supplied local catalogue.

The local API runs on port 5080 and the frontend on port 5173. The frontend proxies `/api` requests to the API. Windows and macOS/Linux launchers are in `scripts/`; production hosting requires client-managed deployment, HTTPS, credentials and backups as described in the handover guide.

## Access control

ASP.NET Core Identity provides cookie authentication. The API checks staff roles, branch/division scopes and customer ownership. The frontend sends a browser-window session identifier with authenticated requests. See the root README for session requirements and the handover guide for administrator setup.
