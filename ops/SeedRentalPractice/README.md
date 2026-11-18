# Repeatable local rental practice data

Run from the repository root while local SQL Server is running:

```sh
dotnet run --project ops/SeedRentalPractice -- --batch 20261112
dotnet run --project ops/SeedRentalPractice -- --batch 20261112 --apply
dotnet run --project ops/SeedRentalPractice -- --batch 20261112 --verify
```

The first command previews the target without changing records. The importer accepts
only the local `Crems` database. It uses the existing configured vehicle service and
bookable branch links, without changing staff permissions or shared business rules.
Connection settings come from the API configuration, user secrets or environment;
when no connection is configured, the repository `.env` supplies the local database
password. Credentials are never printed.

Each batch adds, by default:

| Group | Vehicles | Where to find them | What to practise |
| --- | ---: | --- | --- |
| `FREE` | 30 | Asset register / customer vehicle search | New bookings and quotations |
| `PICK` | 30 | Pickups & returns → Pickup today | Identity/licence, inspection/photos, hire payment, bond, agreement and checkout |
| `RETURN` | 30 | On hire / Due today / Overdue | Return inspection/comparison, charges, hire balance, bond refund/application, acknowledgement and invoice |

Search for **`TRN-20261112`**, or use the identifier for your chosen batch. Select an
appropriate branch/division scope: records are distributed across the existing
bookable vehicle branches. Identifiers such as `TRN-20261112-FREE-001` are internal
fictional asset tags, not vehicle registrations. Names explicitly say TRAINING ONLY.

The confirmed pickups each have a separate vehicle and start at 08:00 Fiji time on
the day the batch is created. Their return date is two days later at 16:00. Pickup
examples include no bond, a bond to collect, prepaid hire and partially paid hire.
The active hires started three days earlier and have return dates of yesterday,
today or tomorrow. They include fully paid, unpaid and partly paid hire balances,
with the refundable bond already held. Their synthetic pre-hire inspections,
photographs, driver details and agreements allow the full return comparison to run.
The signature image says **TRAINING SAMPLE ONLY**. Payments are labelled fictional;
no money is collected or refunded by importing the batch.

New vehicles have no maintenance jobs or outstanding transfers/other rentals,
recorded positive odometer readings in kilometres, and service/insurance/passing dates one year ahead. Initial readings stay below any configured first meter-service threshold. Active return fixtures use the same reading in their outgoing inspections and checkout history. The
importer checks confirmed pickups with the application's actual `PickupReadiness`
rules before committing. Existing assets, maintenance records, bookings and progress
are never cleared or reset. Standard identification, licence, inspection, photograph,
signature and settlement validation still applies during practice.

For self-drive pickup practice, use a fictional numeric licence identifier (for
example `99000001`), class `1`, and a future expiry date. Record readings at or above
the asset's current value. Use unique practice receipt references. Leave damage and
failed checks clear when testing the uninterrupted normal return; deliberately
recorded damage/failure may correctly create maintenance work. The synthetic return
fixtures show their outgoing condition, accessories, readings and photograph in the
comparison step. Customers have no email or portal credentials, so the importer
cannot email real recipients or create a usable customer login.

## Replenish the queues

Re-running the same batch is idempotent: existing assets are skipped, including
ones that you have already picked up or returned. To add a fresh set without
losing previous test results, choose a new batch identifier:

```sh
dotnet run --project ops/SeedRentalPractice -- --batch PRACTICE02 --per-stage 50 --apply
```

This adds 150 fresh vehicles: 50 free, 50 pickups and 50 active returns. Batch names
accept 1–12 letters/numbers; `--per-stage` accepts 1–200. Reuse a batch with `--verify`
to check remaining pickups and active return fixtures. Queues are dated at creation;
use a fresh batch for new “today” pickups on a later day.

These fixtures contribute to local financial totals, reports and fleet counts.
Use this utility for development/training, not a business-production database.
There is intentionally no destructive reset or bulk-delete command.

## Add odometers to an older batch

```sh
dotnet run --project ops/SeedRentalPractice -- --batch 20261112 --record-odometers
```

This backfills untouched zero-reading training vehicles with a positive odometer
and a meter-history entry. It keeps synthetic handover inspections and checkout
records consistent. Vehicles with an existing positive reading, meter history or
staff-entered inspection are preserved. Repeating the command adds no duplicates.
