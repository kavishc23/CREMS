# CREMS

Carpenters Rental and Equipment Management System (CREMS) for Carpenters Fiji. The application provides public rental discovery, customer booking workflows, staff operations, asset management, maintenance, reporting, and administration.

## Technology

- React 19 with TypeScript and Material UI
- ASP.NET Core 10 Web API with ASP.NET Core Identity
- Entity Framework Core 10
- Microsoft SQL Server 2022

## Repository layout

```text
src/CREMS.Api/   ASP.NET Core API and domain model
src/crems-web/   React frontend
tests/           API, browser, and end-to-end tests
ops/             Optional catalogue setup and configuration utilities
docs/            Maintained setup, configuration, and operations guides
```

## Prerequisites

- .NET 10 SDK
- Node.js 22 or newer
- Docker Desktop, or access to Microsoft SQL Server

## Run locally

### Windows client setup

Follow the detailed [Windows client run guide](docs/CLIENT_RUN_GUIDE.md) for first-time installation, everyday startup, optional SMTP, backups and troubleshooting.
An offline [PDF copy](docs/CREMS_Client_Run_Guide.pdf) is included for client handover.

For the easiest first-time setup on Windows, double-click `INSTALL_CREMS.bat` in the repository root. It checks and installs the required .NET 10 SDK and Node.js LTS through WinGet, lets the client choose Docker or an existing SQL Server, restores dependencies, compiles the system, checks API readiness and starts CREMS.

Alternatively, install the prerequisites in [Client handover](docs/CLIENT_HANDOVER.md), open PowerShell in this repository, and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-windows.ps1
```

The first run asks for database and administrator settings. Subsequent runs use the same command. Docker is optional when an existing SQL Server is available. This is a local evaluation setup; email is disabled by the Windows launcher.

### One-command startup

Copy the environment template, fill in the required values, then start the complete local stack:

```bash
cp .env.example .env
./scripts/start-local.sh
```

The script starts SQL Server, restores dependencies when needed, and runs the API and frontend. Press `Ctrl+C` to stop the local stack. See `docs/CLIENT_HANDOVER.md` for client installation, backup, email, and troubleshooting guidance.

### Manual startup

1. Start SQL Server with a password supplied through the environment:

   ```bash
   export CREMS_DB_PASSWORD='replace-with-a-strong-local-password'
   docker compose up -d sqlserver
   ```

2. Export the API connection and initial administrator settings. These values remain in the process environment and are not stored in tracked configuration:

   ```bash
   export ConnectionStrings__DefaultConnection="Server=localhost,1433;Database=Crems;User Id=sa;Password=${CREMS_DB_PASSWORD};TrustServerCertificate=True"
   export BootstrapAdmin__Email='admin@example.com'
   export BootstrapAdmin__Password='replace-with-a-strong-admin-password'
   export BootstrapAdmin__FullName='System Administrator'
   ```

3. Start the API. Database migrations, roles, and the initial administrator are applied automatically:

   ```bash
   dotnet run --project src/CREMS.Api
   ```

4. In another terminal, start the frontend:

   ```bash
   cd src/crems-web
   npm install
   npm run dev
   ```

Open `http://localhost:5173`. During development, Vite proxies `/api` requests to `http://localhost:5080`.

Swagger UI is available at `http://localhost:5080/swagger` in Development. Sign in through its API session panel before using protected endpoints. The OpenAPI document is available at `/openapi/v1.json`.

## Handover documentation

- [Installation, backup and administration](docs/CLIENT_HANDOVER.md)
- [Windows installation and everyday startup](docs/CLIENT_RUN_GUIDE.md) · [PDF copy](docs/CREMS_Client_Run_Guide.pdf)
- [Architecture and repository responsibilities](docs/architecture.md)
- [Organization and asset configuration](docs/configurable-organization-settings.md)
- [Hire pricing and billing units](docs/configurable-hire-pricing.md)
- [Rental defaults](docs/rental-defaults-configuration.md) · [Pricing defaults](docs/rental-pricing-defaults.md)
- [Maintenance operations](docs/maintenance-staff-guide.md)
- [Asset usage and utilisation](docs/asset-usage.md)
- [Staff reports](docs/staff-reports.md) · [Reporting definitions](docs/reporting-metrics.md)
- [Email configuration](docs/STAGING_EMAIL_SETUP.md)

The optional utilities in `ops/SeedCatalogue` and `ops/ExtendRentalCatalogue` configure a local catalogue. Production business records must be entered or imported through an approved client process. For repeatable local pickup/return testing, see [`ops/SeedRentalPractice`](ops/SeedRentalPractice/README.md). It adds explicitly fictional practice vehicles and transactions only when run with `--apply`; it is never executed by application startup.

## Validation

```bash
dotnet test tests/CREMS.Api.Tests
cd src/crems-web
npm ci
npm test
npm run build
```

Run `./scripts/test-coverage.sh` from the repository root to measure backend and frontend code coverage. Results are written under `output/coverage`. Backend collection excludes generated build sources and EF migrations; opt-in SQL integration tests require separate enablement. The script also generates `output/coverage/CREMS_Test_Coverage_Report.html` with per-layer metrics, test counts and the remaining gaps against a 95% line target.

To include isolated SQL integration tests, configure `CREMS_TEST_SQL_CONNECTION` (or the API's local SQL connection in user secrets), then run:

```bash
CREMS_RUN_SQL_TESTS=1 CREMS_RUN_SETTINGS_SQL_TESTS=1 CREMS_RUN_ORGANIZATION_SQL_TESTS=1 ./scripts/test-coverage.sh
```

SQL tests create uniquely named disposable databases and delete those databases afterward; the SQL account needs database creation permissions. Coverage reports distinguish these integration tests from frontend unit/component tests. Passing every test does not mean 95% of application code is covered. The report also audits each measured layer metric and every frontend file against the unchanged 80% green watermark. See `output/coverage/coverage-metric-audit.csv` for uncovered metrics; the separate 95% line target remains stricter. Backend statement/function coverage is unavailable from the selected collector and is not treated as a pass.

The report includes a separate table for `src/components`, immediate `src/pages` files, and `src/pages/maintenance`, matching the frontend coverage directory listing. A directory is green only when statements, branches, functions and lines each reach 80%. To refresh frontend evidence and reuse the existing backend results, run these commands from the repository root:

```bash
npm --prefix src/crems-web run test:coverage -- --reporter=default --reporter=json --outputFile=../../output/coverage/frontend-tests.json
python3 scripts/build-coverage-report.py
```

Frontend workflow tests exercise rendered controls and assert API payloads, validation, permissions, recovery and failure behavior. API calls use test doubles; these tests do not send real messages or modify the training database. Coverage is collected across the whole frontend, including files that no test imports.

Browser and end-to-end setup is documented in [browser checks](tests/browser/README.md) and [end-to-end checks](tests/CREMS.E2E/README.md).

## API session security

Authenticated requests must include an `X-CREMS-Window-Id` header containing a UUID. The web application creates this value in `sessionStorage`. In Postman, add a collection variable such as `windowId` and send `X-CREMS-Window-Id: {{windowId}}` on every request after login.

Security defaults are enforced server-side: a 15-minute authentication timeout, an eight-hour absolute window-session limit, one active browser window per user, a 30-second API execution timeout, and global request rate limiting. A new login in another browser window transfers the active session to that window.

## Local architecture

Local development uses two processes: the API runs on `http://localhost:5080` and Vite runs on `http://localhost:5173`. Vite proxies `/api` requests to the API. No cloud-hosting configuration or generated frontend bundle is required in the repository.

## Engineering rules

- Never commit passwords, connection strings containing real credentials, or customer data.
- Create database changes through reviewed EF Core migrations.
- Enforce permissions in the API; hiding frontend controls is not authorization.
- Use UTC timestamps in storage and convert them only for display.
- Add automated tests for availability, pricing, status transitions, and authorization rules.
