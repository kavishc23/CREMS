# CREMS client handover

## Installation

### Windows laptop (recommended evaluation workflow)

Use Windows 10/11 with PowerShell 5.1 or newer and an internet connection. Install the following from their official websites, then reopen PowerShell:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (SDK, not just runtime).
- [Node.js LTS](https://nodejs.org/) version 22 or newer.
- Either [Docker Desktop](https://www.docker.com/products/docker-desktop/) with WSL 2 and Linux containers enabled, or access to an installed Microsoft SQL Server. SQL Server Express is suitable for a small local evaluation. SQL Server Developer and the supplied Docker image are for development/testing, not a production licence.

Clone the repository or extract its source ZIP, open PowerShell in the folder containing README.md, and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-windows.ps1
```

For Docker, open Docker Desktop first. Choose `docker` and supply a strong database password. For an installed server choose `existing` and provide a connection string, for example `Server=localhost\SQLEXPRESS;Database=Crems;Integrated Security=True;TrustServerCertificate=True`. The Windows account running CREMS must have permission to create/update that database. Use a dedicated empty database for evaluation, not an existing business database.

Enter the initial administrator email and a strong password when prompted. Secrets are masked while entering them and saved in the ignored `.env.windows.json` file. This file contains plaintext credentials: keep it private and exclude it when sharing a ZIP. The script restores dependencies, builds the API, waits for SQL Server/API readiness, and starts the frontend. Open `http://localhost:5173` and sign in. Development startup automatically populates a new database with demonstration data; use fictional information during evaluation. Additional seed utilities under `ops/` are optional.

Use the same command each time. Press Ctrl+C to stop the application. The Docker database remains running and retains data; stop it separately with `docker compose stop sqlserver` after closing CREMS. Never use `docker compose down -v` unless intentionally deleting the database. If a process remains after closing the terminal, close its terminal or stop that specific process before restarting.

Docker is **not required** if SQL Server is installed locally or hosted elsewhere. It only supplies the database; the API and frontend run directly on Windows. This command is intended for local installation and evaluation, not unattended production hosting. Email is disabled, so delivery and email OTP workflows require the email configuration below and a separate configured API startup.

### macOS/Linux

Install Docker Desktop, the .NET 10 SDK, and Node.js 22 or newer. Clone the repository, then run:

```bash
cp .env.example .env
```

Open `.env` and replace every required placeholder. Keep this file private. Start CREMS with:

```bash
./scripts/start-local.sh
```

Open `http://localhost:5173`. Press `Ctrl+C` in the startup terminal to stop the application. SQL Server data remains in the Docker volume between restarts.

## Administrator setup

Set `CREMS_ADMIN_EMAIL`, `CREMS_ADMIN_PASSWORD`, and `CREMS_ADMIN_FULL_NAME` in `.env` before the first start. CREMS creates this administrator automatically when the database is initialized. After confirming sign-in, change the account password in CREMS and replace the bootstrap value in `.env` with a different long random value; existing passwords are not overwritten during normal startup. Do not share `.env` or commit it to Git.

## Database backups

On Windows with Docker, use PowerShell to create and copy a backup (the container already has its database password):

```powershell
$container = docker compose ps -q sqlserver
docker exec $container mkdir -p /var/opt/mssql/backup
docker exec $container sh -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -Q "BACKUP DATABASE [Crems] TO DISK = N''/var/opt/mssql/backup/crems.bak'' WITH INIT"'
docker cp "${container}:/var/opt/mssql/backup/crems.bak" .\crems-backup.bak
```

For installed SQL Server, use SQL Server Management Studio's database **Tasks > Back Up** action. Also back up uploaded files under `src/CREMS.Api/App_Data` and API data-protection keys; a SQL backup does not include those files. Keep all backups outside source control.

Create a SQL Server backup before upgrades or major data imports:

```bash
docker compose exec sqlserver mkdir -p /var/opt/mssql/backup
docker compose exec sqlserver /bin/bash -lc '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -Q "BACKUP DATABASE [Crems] TO DISK = N'"'"'/var/opt/mssql/backup/crems.bak'"'"' WITH INIT"'
docker cp "$(docker compose ps -q sqlserver):/var/opt/mssql/backup/crems.bak" ./crems-backup.bak
```

Store backup files outside the repository and test restoration periodically. To restore, stop the API, copy the backup into the SQL Server container, and use SQL Server `RESTORE DATABASE` commands. A database administrator should review restore commands before running them against a live system.

## Email configuration

Email is disabled by default. For Brevo, set `EMAIL_ENABLED=true`, `EMAIL_API_KEY`, `EMAIL_FROM_ADDRESS`, and `EMAIL_FROM_NAME`. In development, set `EMAIL_REDIRECT_ALL_TO` to a controlled mailbox so customer messages cannot be sent accidentally. SMTP can be used instead by filling the `EMAIL_SMTP_*` settings and leaving `EMAIL_API_KEY` blank.

Never place email credentials in `appsettings.json` or commit them to Git.

## Troubleshooting

- **Frontend cannot reach the API:** confirm the API reports `http://localhost:5080` in the terminal and open `http://localhost:5080/api/health`.
- **SQL Server does not become healthy:** run `docker compose logs sqlserver`; the usual cause is a weak or missing `CREMS_DB_PASSWORD`.
- **Port already in use:** stop the conflicting service or change `CREMS_API_URL`/`CREMS_FRONTEND_URL` in `.env`. If changing the API port, also update the Vite proxy in `src/crems-web/vite.config.ts`.
- **Administrator cannot sign in:** confirm the bootstrap values were present on the first start and meet the password rules. Existing administrator passwords are not overwritten automatically.
- **Email remains queued:** verify the sender is approved by the provider and review the API terminal for delivery errors.
- **Reset the local database:** stop CREMS and run `docker compose down -v`. This permanently deletes local SQL data; restore from a backup if needed.

## Handover checklist

1. Store `.env` and backup files in the client's password manager or secure storage.
2. Confirm administrator sign-in and change the temporary password.
3. Verify `/api/health`, public catalogue images, one customer booking, and one staff workflow.
4. Configure automated database backups before production use.
5. Keep the operating system, Docker, .NET runtime, Node.js, and application dependencies patched.
