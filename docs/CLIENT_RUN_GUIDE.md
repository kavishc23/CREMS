# CREMS — client installation and operating instructions

This guide explains how to install and run CREMS on a Windows laptop for local evaluation. Keep this document with the repository. No Azure account, domain name, tunnel, or cloud subscription is needed.

## 1. What you are installing

CREMS has three parts:

| Part | Purpose | Local address |
| --- | --- | --- |
| Web application | Customer and staff screens | http://localhost:5173 |
| API | Sign-in, permissions and business operations | http://localhost:5080 |
| SQL Server | Stores accounts, bookings and other records | Docker: localhost,1433; installed SQL Server: your configured instance |

Keep the startup terminal open while using CREMS. Closing the laptop, shutting it down, or stopping the application can interrupt access. `localhost` means this laptop; this setup does not publish CREMS to other computers.

SMTP is optional. You can install, start, and explore CREMS without configuring email. Features that send email—including email verification codes, password changes requiring OTP, and notifications—need a working email provider. Codes are not bypassed when email is disabled.

## 2. Install the prerequisites once

Use a supported Windows laptop with PowerShell, internet access, and enough free storage for development tools and database images. Have Windows administrator access available for installing prerequisites. The normal CREMS startup command does not require an elevated terminal.

Install:

1. **.NET 10 SDK**, including its build tools. Installing only the runtime is insufficient.
2. **Node.js 22 or newer**, including npm.
3. One database option from the next section.
4. Optionally **Git for Windows**, if receiving updates through Git. A source ZIP can also be used.

Use the official download links in [Client handover](CLIENT_HANDOVER.md). Follow each installer's prompts, then close and reopen PowerShell so it can find the installed commands.

Verify installation:

```powershell
dotnet --version
node --version
npm.cmd --version
```

The .NET version must match the repository's .NET 10 requirement. If any command is missing, reopen PowerShell or repair that tool's installation before continuing.

## 3. Choose your database

### Option A: Docker Desktop

Use this option if you do not already have SQL Server configured. Install Docker Desktop, complete its WSL 2 setup if prompted, select Linux containers, and wait until its engine is running.

Confirm Docker is available:

```powershell
docker info
docker compose version
```

CREMS downloads the SQL Server image on first startup. This first download can take several minutes. The database is saved in a Docker volume and persists when the application stops. Port 1433 must be free.

### Option B: Existing SQL Server — Docker not required

Use this option if SQL Server is already installed locally or available on an approved server. Ask your IT contact for the server/instance and a connection string. Use a dedicated evaluation database. The startup account needs permission to create the database or apply its schema migrations.

For a local SQL Server Express installation using your Windows account, a typical connection string is:

```text
Server=localhost\SQLEXPRESS;Database=Crems;Integrated Security=True;TrustServerCertificate=True
```

The instance name may differ. Windows authentication uses the identity running PowerShell. SQL authentication instead requires the supplied database username and password. Do not paste the connection string into an email or support screenshot.

## 4. Obtain and open the repository

Extract the repository ZIP into a normal writable folder, for example `C:\CREMS`. If using Git, clone the repository using the access provided by the supplier. Do not run from inside the ZIP archive.

Open PowerShell and change to the repository root:

```powershell
cd C:\CREMS
dir
```

You should see `README.md`, `compose.yaml`, `scripts`, and `src`. If the ZIP created an extra parent folder, change into the folder containing those files.

## 5. First-time setup and startup

Open Docker Desktop first if using Docker. From the repository root, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-windows.ps1
```

The execution-policy option applies to this PowerShell process. If your organization blocks script execution through policy, ask IT to approve the script.

Answer the setup prompts:

| Prompt | What to enter |
| --- | --- |
| Database | `docker` or `existing` |
| Initial administrator email | An address controlled by the client's administrator |
| Initial administrator password | At least 10 characters, including uppercase, lowercase, a number and a symbol |
| New SQL Server password (Docker only) | At least 12 characters, including uppercase, lowercase, a number and a symbol |
| Connection string (existing SQL Server only) | The connection string supplied or approved by IT |

Passwords and the connection string are hidden while typing. Save the credentials securely. The script stores settings in `.env.windows.json` in the repository root. This is a private plaintext file excluded from Git. Do not send it with the source repository or include it in a shared ZIP. The Windows launcher uses this file; the `.env` template is used by the separate macOS/Linux launcher.

The launcher then:

1. Starts and waits for the Docker database, if selected.
2. Restores API dependencies.
3. Installs frontend dependencies when missing or when the dependency lock file changes.
4. Compiles and starts the API. The API automatically applies database migrations.
5. Waits for the API health endpoint, then starts the web application.

First startup needs internet access for packages and Docker images. Wait for the frontend to report its local address. Open **http://localhost:5173** in a browser.

Development startup populates a new database with demonstration information. Use fictional customer details during evaluation. This setup is intended for evaluation; production installation requires a separate hosting and operational configuration.

## 6. Sign in and explore

Choose the staff sign-in option and use the initial administrator email and password entered during setup. Do not assume demonstration passwords are valid for your administrator account.

The bootstrap super administrator can sign in without a login email code. Other accounts may require email MFA according to their role and account settings. Configure optional email before evaluating those accounts or password-change OTP flows.

Suggested evaluation sequence:

1. Review organization, divisions, branches and service settings.
2. Review the asset register and the public rental catalogue.
3. Submit a fictional customer booking request.
4. Review the request, prepare pricing and follow its configured approval process.
5. Confirm the booking and complete the required pickup checks.
6. Complete a return, review charges, record settlement and inspect the invoice.
7. Review maintenance, reports and staff permissions.

Availability, customer eligibility, document checks, payments and safety release rules can block a workflow legitimately. Read the message and resolve the underlying requirement. Do not change the database directly to bypass a blocked step.

## 7. Daily startup and shutdown

For every later session, open Docker Desktop if applicable, open PowerShell in the repository root, and run the same startup command from section 5. Existing settings and database records are reused.

Keep the terminal open. To stop CREMS, press **Ctrl+C** in that terminal and confirm termination if Windows asks. The launcher attempts to stop its API process. Close any remaining CREMS terminal before restarting.

Docker SQL Server remains running. To stop it, open Docker Desktop and stop the project's SQL Server container. Stopping a container preserves its data. Do not delete the volume or use `docker compose down -v` during normal shutdown.

Changing the bootstrap password in `.env.windows.json` does not reset an existing administrator's password. Use the application's password workflow with email configured, or contact the supplier for account recovery.

## 8. Optional SMTP email

Skip this section for basic local evaluation. Email is disabled by default by the Windows launcher. The `-EnableEmail` flag explicitly enables configured email for that run.

Ask your email provider or IT administrator for the SMTP hostname, port, username, password/app password, approved sender address, and STARTTLS requirements. CREMS uses the .NET SMTP client with TLS controlled by `Email__UseSsl`; confirm your provider supports the chosen connection method.

Open PowerShell in the repository root. Set the following for the current terminal, replacing example values with your provider's actual settings:

```powershell
$env:Email__PreferHttpApi = 'false'
$env:Email__Host = 'smtp.example.com'
$env:Email__Port = '587'
$env:Email__UseSsl = 'true'
$env:Email__Username = 'your-smtp-username'
$env:Email__FromAddress = 'approved-sender@example.com'
$env:Email__FromName = 'Carpenters Rentals'
$env:Email__RedirectAllTo = 'your-test-mailbox@example.com'
$smtpSecret = Read-Host 'SMTP password or app password' -AsSecureString
$env:Email__Password = [System.Net.NetworkCredential]::new('', $smtpSecret).Password
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-windows.ps1 -EnableEmail
```

Use a mailbox you control for `Email__RedirectAllTo`: test messages and verification codes are redirected there. Do not use real customer addresses for evaluation. The email provider must approve the sender address. If your provider requires OAuth-only SMTP, consult IT about a supported relay or the application's Brevo HTTP delivery option.

These environment settings last for that PowerShell session and are inherited by the launcher. They are not saved in `.env.windows.json`. Re-enter them after opening a new terminal, or have IT store and load them securely. Do not commit an email password. After stopping email-enabled CREMS, close that terminal to discard its process environment.

Run the command without `-EnableEmail` to return to email-disabled evaluation. Enabling email does not guarantee delivery: verify receipt of a test message before using OTP or customer notifications.

## 9. Back up before updates or important evaluation work

Back up both the SQL database and uploaded documents/photos. Source code alone is not a backup of application records.

- For installed SQL Server, use SQL Server Management Studio: database > Tasks > Back Up. Ask IT to verify the backup and restore procedure.
- For Docker SQL Server, use the backup procedure in [Client handover](CLIENT_HANDOVER.md), or ask IT to connect a database management tool to `localhost,1433`.
- Copy `src/CREMS.Api/App_Data` to secure backup storage after stopping the application. This contains uploaded application files.
- Preserve API data-protection keys and private configuration with access restricted to authorized administrators. Default development key locations depend on the Windows account running the API.
- Store backups outside the repository and verify they can be restored to a separate database.

Do not replace `.env.windows.json`, delete database volumes, or overwrite uploaded files when receiving an update. Stop CREMS, take a backup, apply the supplied update, and run the launcher again. Migrations run automatically on startup; keep the backup until the update has been verified.

## 10. Troubleshooting

| Symptom | Action |
| --- | --- |
| `dotnet`, `node`, or `npm.cmd` missing | Install the prerequisite, then reopen PowerShell. |
| `vite: command not found` | Run the launcher from the repository root so dependencies are installed. If dependencies were manually damaged, remove only `src/crems-web/node_modules/.crems-lock-hash`, then rerun the launcher. |
| Script cannot be found | Check that the current folder contains `scripts/start-windows.ps1`. |
| Docker cannot connect | Open Docker Desktop and wait for its Linux engine; check WSL setup. |
| SQL Server does not become healthy | Check Docker logs. Confirm password complexity, available memory and port 1433. An existing database volume retains its original password even if settings are edited. |
| SQL login or migration error | Confirm the instance, authentication credentials and migration permissions with IT. |
| Port 5080 or 5173 occupied | Stop the earlier CREMS process or identify the conflicting application before starting again. |
| Webpage says API unavailable | Check the API terminal. Open http://localhost:5080/api/health; after successful startup it should report healthy. This endpoint confirms API responsiveness, not every business workflow. |
| Agreement or workflow endpoint missing | Stop and restart the API through the launcher after an update, then refresh the browser. |
| Administrator sign-in fails | Use the email/password from the original setup. Editing bootstrap settings does not reset an existing account. |
| Email code does not arrive | Confirm `-EnableEmail`, SMTP credentials, sender approval, redirect mailbox and spam folder. Review delivery errors in the API output. |
| Session unavailable or signed out | Refresh and sign in through the web application. Opening a protected API URL directly does not reproduce the application's browser-window session headers. |
| Asset cannot be picked up | Resolve open hire, transfer, maintenance or safety-release requirements shown by CREMS. |
| Settings file is invalid | Correct `.env.windows.json` using a JSON-aware editor. Preserve the original privately before editing; backslashes and quotation marks need JSON escaping. |

For support, provide the action attempted, the visible message and the relevant terminal error. Remove passwords, connection strings, OTP codes, private customer information and document images before sharing.

## 11. What to retain and what to share

Retain the repository source, private local settings, database backups and uploaded-file backups. When sharing source, use a Git checkout or source archive that excludes ignored files. Do not ZIP the entire working folder without reviewing it: ignored files can still contain credentials, customer uploads, build output and local dependencies.

The supplied Windows launcher has been reviewed on macOS; execution on a Windows laptop still needs to be verified. The API and frontend compilation results do not replace that Windows installation check.
