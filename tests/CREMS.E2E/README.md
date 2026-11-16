# Organization and Operations SQL/browser tests

This disposable test host runs the production React build, real ASP.NET controllers,
Identity password login, cookies, window-session middleware, authorization policies,
and SQL Server migrations. Browser API traffic is not mocked.

Prerequisites: .NET 10, Node, Chrome, Playwright and a reachable SQL Server account
allowed to create and delete a test database. Set `CREMS_TEST_SQL_CONNECTION` securely
in the environment. Set `PLAYWRIGHT_MODULE` to the Playwright package path if needed.
Do not put credentials in command arguments or tracked files.

From the repository root:

```powershell
npm --prefix src/crems-web run build
dotnet build tests/CREMS.E2E --artifacts-path tmp/e2e-build
dotnet tmp/e2e-build/bin/CREMS.E2E/debug/CREMS.E2E.dll (Get-Location).Path
```

Every invocation creates a uniquely named `CremsWorkspaceE2E_...` database and
deletes that exact database in `finally`. It never selects the source connection's
database as its working database. Test passwords are generated per run and passed
to the child browser process through the environment. Output and screenshots in
`tmp/workspace-e2e` contain only fictional test records.

The host deliberately omits production bootstrap seeding, background automation,
external email delivery and deployment HTTPS configuration. It is not a deployment,
MFA/email-delivery or load test. Killing the host abruptly can bypass cleanup; inspect
only databases with the test prefix before manually removing a leftover database.

The browser script covers administrator organization writes and reloads, category
fields, branch service reactivation and nullable cutoffs, operations task completion,
delivery completion evidence, supplier creation, business-tools navigation, mobile
layout, anonymous access and cross-role/cross-branch write restrictions.

For the connected practical-assessment scenario, set `CREMS_E2E_ASSESSMENT=1`
before running the same host. This uses fictional customer and manager accounts
to verify configuration, fees, approval, customer acceptance, availability filters
and audit history. Results are written under `output/assessment-flow`. SMTP workers
remain disabled in this host; actual loopback SMTP acceptance, rejection and retry
behavior are covered separately by `AssessmentEmailDeliveryTests`. No external
customer email is sent by either test workflow.
