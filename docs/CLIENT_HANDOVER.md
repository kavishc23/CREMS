# CREMS client handover

## Installation

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
