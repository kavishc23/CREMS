# CREMS

Car and Rental Equipment Management System for Carpenters Fiji. The application provides public rental discovery, customer booking workflows, staff operations, asset management, maintenance, reporting, and administration.

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
ops/             Optional data-seeding and configuration utilities
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

Install the prerequisites in [Client handover](docs/CLIENT_HANDOVER.md), open PowerShell in this repository, and run:

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

Swagger UI is available at `http://localhost:5080/swagger` while the API runs in Development.
Use the **API session** panel to sign in, verify/resend MFA codes, view the account and roles,
refresh your session, or sign out. Credentials are not stored in browser storage.
The banner shows the connected environment, database server and database name, without credentials.
Review it and check **Allow data changes** before executing non-authentication write requests.
This browser-side guard is a testing aid; server authorization still applies.
Examples for customer creation, maintenance creation and booking updates use fictional data;
replace zero GUIDs with accessible record IDs and review dates and rates.
Expand an endpoint and select **Try it out**, then **Execute**. For protected endpoints,
you can alternatively execute `POST /api/auth/login` in Swagger with your email and password (and
`POST /api/auth/mfa/verify` if prompted). Subsequent requests use the session cookie;
Swagger supplies the required browser-window header automatically.
The OpenAPI document is at `http://localhost:5080/openapi/v1.json`.
Use the filter to find endpoint groups. Lock icons indicate required authentication,
not current session status. This API uses HttpOnly login cookies, not a bearer token;
the Swagger Authorize dialog cannot sign you in or out. Use the login endpoint above
and `POST /api/auth/logout` (staff) or `POST /api/customer-account/logout` (customers).
A 401 means you need a valid session in this browser window; a 403 means your account
lacks the necessary role, permission, or resource access.

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
