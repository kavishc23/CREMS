# Realistic local demo dataset

Run from the repository root:

```sh
./.dotnet/dotnet run --project ops/SeedDemo -p:UseSharedCompilation=false -- --apply
```

This imports fictional, linked scenarios into **local `Crems` only**, using the API's development configuration and user secrets. It does not start the API or its email workers. Automatic change notifications are suppressed, and no outbound email or SMS records are inserted. Contacts use `example.invalid` addresses.

The import adds 48 assets, 48 customers, 36 staff/customer accounts, 144 completed hires and 24 current/future hires, invoices with matching lines/payment allocations, agreements, inspections, quote revisions, workshop history and active repairs, parts usage, inventory, purchase orders, suppliers, personnel qualifications/timesheets, support cases, approvals, transfers, tasks, alerts and in-app notifications. Reports and dashboards derive their figures from these transactions. Workshop photos are clearly labelled illustrative reference photos, not actual fault evidence.

New records have `DEMO-` references or explicit demo labels. Existing records and organizational/security settings are preserved. New pricing rules apply only to the added assets. New accounts have random undisclosed passwords; use the existing administrator reset workflow if demo sign-in is needed. No real driver-licence documents or credentials are fabricated.

Database writes use one serializable transaction. The `demo.realistic-september-v1` system setting records a completed import and prevents duplicate imports on rerun. Dates are relative to the initial import date. An import rollback does not delete any reference-photo files copied to the maintenance storage folder; rerunning safely reuses them.

This is a data import command, not a test suite. Inspect its committed counts to see the records actually added.
