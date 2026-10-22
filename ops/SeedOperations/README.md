# Local operational records

`dotnet run --project ops/SeedOperations -- --inspect` prints inventory and configuration counts.

`dotnet run --project ops/SeedOperations -- --apply` applies pending schema updates, removes obsolete fixture labels, restores booking configuration for the four seeded branches, and creates operational scenarios across the configured divisions. Original label values are saved under the ignored `outputs/operational-label-backup-*.json` path. The import marker prevents duplicate scenarios.

`dotnet run --project ops/SeedOperations -- --verify` checks typed codes and QR payloads against the local database and checks the running public Shipping API on port 5080.

This tool refuses remote databases. The records are synthetic local development fixtures; no customer messages or emails are sent. Realistic display labels do not represent actual business transactions.
