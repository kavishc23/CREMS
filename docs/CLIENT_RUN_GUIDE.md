# CREMS Client Installation and Testing Guide

This guide explains how to install and run the Carpenters Rental and Equipment Management System (CREMS) on a Windows laptop for client testing.

CREMS runs locally on the laptop. No Azure account, public domain or cloud subscription is required. Use fictional customer, booking, licence and payment information during testing.

## 1. Before you begin

You need:

- A Windows 10 or Windows 11 laptop.
- Internet access for the first installation.
- Permission to install applications on the laptop.
- Access to the CREMS GitHub repository.
- Approximately 15 GB of free disk space when using Docker Desktop.

Accept the GitHub collaborator invitation, open the CREMS repository and use the `main` branch. Either clone the repository with GitHub Desktop/Git or download and extract the source ZIP into a normal writable folder such as `C:\CREMS`.

Do not run CREMS from inside a ZIP file. The repository folder should contain `INSTALL_CREMS.bat`, `README.md`, `compose.yaml`, `src` and `scripts`.

## 2. Choose a database option

The installer asks you to choose one database option.

### Option D - Docker Desktop

Choose this option when the laptop does not already have an approved SQL Server installation. The installer can install Docker Desktop and use the supplied SQL Server container. Docker may require administrator approval, WSL 2 setup and a Windows restart.

Docker is for local testing and is not required when an existing SQL Server is available.

### Option E - Existing SQL Server

Choose this option when the organization already has SQL Server or SQL Server Express. Ask IT for an approved connection string and ensure the account can create the CREMS database and apply migrations.

A typical local SQL Server Express connection string using Windows authentication is:

```text
Server=localhost\SQLEXPRESS;Database=Crems;Integrated Security=True;TrustServerCertificate=True
```

The actual server and instance name may differ. Treat database connection strings as confidential.

## 3. Install CREMS

1. Open the extracted or cloned CREMS repository folder.
2. Double-click `INSTALL_CREMS.bat`.
3. Approve Windows installation prompts when requested.
4. Choose `D` for Docker or `E` for an existing SQL Server.
5. Enter the initial administrator email address.
6. Enter a strong administrator password with at least 10 characters, uppercase and lowercase letters, a number and a symbol.
7. If using Docker, enter a separate SQL Server password with at least 12 characters, uppercase and lowercase letters, a number and a symbol.
8. If using an existing SQL Server, enter the connection string supplied by IT.
9. Wait while the installer restores dependencies, compiles CREMS, initializes the database and checks the API.

The installer automatically checks for the .NET 10 SDK and Node.js 22 or newer. Missing versions are installed through Windows Package Manager. If Windows asks for a restart, restart the laptop and run `INSTALL_CREMS.bat` again. Completed installations will be reused.

Private settings are saved in `.env.windows.json` in the repository root. This file is excluded from Git but contains plaintext credentials. Do not email it, upload it or include it in a shared ZIP.

## 4. Open CREMS and sign in

When startup is complete, open:

- CREMS website: `http://localhost:5173`
- API health check: `http://localhost:5080/api/health`

Keep the installer terminal open while using CREMS. Closing the terminal stops the web application and API.

Select staff sign-in and use the administrator email and password entered during setup. A new development database includes demonstration information for evaluation. Continue to use fictional information only.

## 5. Start and stop CREMS later

For each later session:

1. Open the repository folder.
2. Double-click `INSTALL_CREMS.bat` again.
3. Existing dependencies, private settings and database records will be reused.
4. Open `http://localhost:5173` if the browser does not open automatically.

To stop CREMS, return to the terminal and press `Ctrl+C`. If Docker is being used, its SQL Server container can remain running and the database records will be preserved.

Do not run `docker compose down -v`; the `-v` option permanently removes the local database volume.

## 6. Client testing and UAT

Use the supplied User Acceptance Testing workbook to record the result of each test. Complete the tests using the final `main` branch.

For each issue, record:

- The test case or affected page.
- The steps performed.
- The expected result.
- The actual result or visible message.
- A screenshot where possible.

Do not include real customer data, passwords, one-time codes, connection strings or confidential documents in screenshots.

After completing the UAT, review the recorded results and sign the UAT sign-off section to confirm acceptance or identify any remaining agreed actions.

## 7. Optional email configuration

Email is not required for basic installation and general workflow testing. However, email verification, password-change OTP and notification delivery require a configured provider.

Run CREMS normally without email unless email testing has been agreed. IT can configure an approved SMTP provider or Brevo API credentials later. Email passwords and API keys must be stored outside tracked configuration and must never be committed to Git.

When email is enabled for testing, redirect messages to a controlled test mailbox and verify delivery before testing OTP workflows.

## 8. Backup guidance

Before updates or important testing, back up:

- The CREMS SQL database.
- Uploaded files under `src\CREMS.Api\App_Data`.
- Private configuration and data-protection keys in secure storage.

For an installed SQL Server, use SQL Server Management Studio and select the CREMS database, then **Tasks > Back Up**. For Docker, ask the project team or IT to create a SQL Server backup from the container.

Store backups outside the repository and verify that the backup can be restored. Source code alone is not a backup of application records or uploaded files.

## 9. Troubleshooting

| Problem | Recommended action |
| --- | --- |
| WinGet is missing | Install or update **App Installer** from Microsoft Store, then rerun the installer. |
| Windows requests a restart | Restart the laptop and run `INSTALL_CREMS.bat` again. |
| Docker is not ready | Open Docker Desktop, complete WSL 2 or first-run setup and wait for the engine before retrying. |
| SQL Server does not start | Confirm the SQL password meets the complexity requirements and port 1433 is free. |
| Existing SQL connection fails | Ask IT to verify the server, authentication and database creation/migration permissions. |
| Port 5080 or 5173 is occupied | Close any earlier CREMS terminal or stop the application using that port. |
| Website says the API is unavailable | Review the terminal error and open `http://localhost:5080/api/health`. |
| Administrator cannot sign in | Use the original setup email and password. Editing the settings file does not reset an existing account. |
| Email OTP does not arrive | Confirm email has been intentionally configured and check the test mailbox and API output. |
| Pickup or return is blocked | Read the CREMS message and resolve missing eligibility, payment, inspection, maintenance or safety requirements. |

If assistance is required, provide the visible error, action attempted and a screenshot with all confidential information removed.

## 10. Important limitations

This installer provides a local client testing environment. `localhost` means CREMS is available only on the laptop running it. The setup is not a production deployment and does not provide public internet access, automated enterprise backups, high availability or centralized monitoring.

Production hosting, security operations, backup scheduling and support arrangements should be agreed separately before using CREMS with live business or customer data.
