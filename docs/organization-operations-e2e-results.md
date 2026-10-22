# Organization and Operations centre test results

Date: 16 October 2026 (Pacific/Fiji)

## Result

25 live browser/API checks passed against SQL Server 2022 in a fresh disposable
database. No browser JavaScript crashes were observed. The test database was
deleted after the run. Existing application records were not used or modified.

The production frontend build was served by an isolated ASP.NET host using the
real controllers, Identity password authentication, cookies, window-session
middleware, authorization policies and migrated SQL schema. Requests were not
intercepted or mocked. Test accounts represented Super Administrator, Branch
Manager and Rental Officer roles. Manager permissions explicitly included branch
configuration and asset-category configuration.

## Verified workflows

- Real browser login and protected Organization navigation.
- Division creation in the browser and persistence after reload.
- Branch service assignment, deactivation/reactivation without duplicate rows,
  and clearing an optional pickup cutoff to SQL null.
- Category creation and custom-field saving in the browser, with reload verification.
- Task completion and removal from the active work queue.
- Delivery completion requiring a recipient, and storage of completion/evidence.
- Supplier creation in the browser and retrieval through the real workspace API.
- Navigation through all twelve Business tools sections and mobile navigation.
- Anonymous requests rejected for Organization and Operations workspace data.
- Rental officers restricted from other divisions and division/supplier creation.
- Managers blocked from reading another branch configuration, changing another
  branch status, or changing category fields in another division.
- Rejected writes leave the database unchanged; authorized manager field edits
  within the assigned division persist successfully.

## Defects reproduced and fixed

1. Non-administrator division listing returned HTTP 500 on SQL Server. Scope
   filtering now happens before projection into response records, allowing EF to
   translate the query.
2. Branch status changes lacked a branch-scope check. The endpoint now rejects
   requests outside the caller's assigned branches.
3. Category-field updates lacked a division-scope check. The endpoint now checks
   the category's division before altering field definitions.

The live suite also verified the earlier frontend fixes for inactive-service
reactivation and nullable pickup cutoffs.

## Additional regression evidence

- API regression suite after the controller changes: 296 passed, one SQL test
  skipped by its opt-in gate.
- That SQL integration test was separately enabled and passed against its own
  disposable database earlier in this session, exercising maintenance persistence,
  stock concurrency, completion/release and financial reconciliation.
- Previous frontend checks: 23 tests and the production build passed.

## Remaining limits and risks

This is a verified set of end-to-end paths, not an exhaustive production sign-off.
Quote creation/conversion, multi-stage approvals, purchase-order processing,
personnel scheduling, transfer lifecycles and all combinations of organization
editing/validation still need dedicated live scenarios. Navigation alone does not
verify each workflow. Administrator MFA, email delivery, production startup
workers, HTTPS deployment, all permission combinations and load testing were not
part of this isolated host.

Package restore reported moderate/high security advisories for the transitive
`System.Security.Cryptography.Pkcs` package version 9.0.4 (NU1902/NU1903).
That dependency was not changed as part of this functional test pass.

## Reproduce

See [the E2E harness instructions](../tests/CREMS.E2E/README.md).
Machine-readable results and fictional-data screenshots are generated under
`tmp/workspace-e2e/`. Source changes have not been deployed or used to restart the
existing application service.
