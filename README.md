# CREMS

Car and Rental Equipment Management System for Carpenters Fiji. This repository contains the approved .NET-stack foundation for the CS400 Industry Experience Project.

## Technology

- React 19 with TypeScript and Material UI
- ASP.NET Core 10 Web API with ASP.NET Core Identity
- Entity Framework Core 10
- Microsoft SQL Server 2022

## Repository layout

```text
src/CREMS.Api/   ASP.NET Core API and domain model
src/crems-web/   React frontend
docs/            Project decisions and requirement records
```

## Prerequisites

- .NET 10 SDK
- Node.js 22 or newer
- Docker Desktop, or access to Microsoft SQL Server

## Run locally

1. Copy `.env.example` to `.env` and replace the example database password.
2. Update the matching password in the API connection string using user secrets:

   ```bash
   dotnet user-secrets init --project src/CREMS.Api
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=Crems;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True" --project src/CREMS.Api
   ```

3. Start SQL Server and create the first migration:

   ```bash
   docker compose up -d
   dotnet tool restore
   dotnet tool run dotnet-ef database update --project src/CREMS.Api
   ```

4. Store the initial administrator securely outside source control:

   ```bash
   dotnet user-secrets set "BootstrapAdmin:Email" "admin@example.com" --project src/CREMS.Api
   dotnet user-secrets set "BootstrapAdmin:Password" "REPLACE_WITH_A_STRONG_PASSWORD" --project src/CREMS.Api
   dotnet user-secrets set "BootstrapAdmin:FullName" "System Administrator" --project src/CREMS.Api
   ```

5. Start the API. The migration, roles, and administrator are applied automatically:

   ```bash
   dotnet run --project src/CREMS.Api
   ```

6. In another terminal, start the frontend:

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

## Current status

### API session security

Authenticated requests must include an `X-CREMS-Window-Id` header containing a UUID. The web application creates this value in `sessionStorage`. In Postman, add a collection variable such as `windowId` and send `X-CREMS-Window-Id: {{windowId}}` on every request after login.

Security defaults are enforced server-side: a 20-minute idle authentication timeout, an eight-hour absolute window-session limit, one active browser window per user, a 30-second API execution timeout, and global request rate limiting. A new login in another browser window transfers the active session to that window.

The repository contains the technical foundation, responsive application shell, identity configuration, core roles, initial domain entities, SQL Server configuration, and a protected asset endpoint. Feature workflows remain intentionally unimplemented until the client validates the business rules in `docs/requirements-register.md`.

## Engineering rules

- Never commit passwords, connection strings containing real credentials, or customer data.
- Create database changes through reviewed EF Core migrations.
- Enforce permissions in the API; hiding frontend controls is not authorization.
- Use UTC timestamps in storage and convert them only for display.
- Add automated tests for availability, pricing, status transitions, and authorization rules.
