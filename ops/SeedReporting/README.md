# Current-month reporting examples

Run from the repository root:

```sh
dotnet run --project ops/SeedReporting -- --apply
```

This command inserts linked, fictional bookings, invoice lines, payment allocations and maintenance jobs into **local `Crems` only**. It uses the API development connection configuration, rejects remote database servers, suppresses notifications, and never creates real customer credentials or sends money. References start with `DEMO-RPT-` and contacts use `example.invalid`.

The import is transactional and once per Fiji calendar month. Rerunning it during the same month changes nothing. The reporting page defaults to the current month, so these records appear in its Hires, Invoices, Payments and Maintenance tabs. Existing records are preserved. Do not run demo importers against a production copy of customer data.
