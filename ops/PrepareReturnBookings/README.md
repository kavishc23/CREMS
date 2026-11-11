# Return-ready presentation bookings

Local Crems database only. `--apply` creates two fictional, signed, active vehicle rentals, due at 23:59 today in Fiji. It uses available catalogue vehicles with no overlapping confirmed or active hires. Names, identification, signatures and receipts are illustrative; no actual hires or payments occurred. This utility sends no notifications and does not change existing bookings. Sample signatures are the bundled PNGs, not real customer consent.

Run with the repository's .NET SDK:

```
./.dotnet/dotnet run --project ops/PrepareReturnBookings -- --apply
./.dotnet/dotnet run --project ops/PrepareReturnBookings -- --verify
```

Apply is idempotent for today's references. Verify checks that pickup is complete, signatures exist and return is pending; it runs both actual controller return operations inside transactions and rolls them back. Thus the bookings remain awaiting return. It exports agreement JSON to `tmp/return-bookings-qa` for the browser PDF check. Do not run apply on another day unless two additional sample rentals are wanted.

Created on 11 November 2026:

| Booking | Customer | Asset | Pickup meter | Hire paid |
| --- | --- | --- | --- | --- |
| BK-20261111-RET-01 | Arun Prasad | CM-HAT-002 | 22,460 km | FJD 264.50 |
| BK-20261111-RET-02 | Mere Williams | CM-PUP-001 | 32,870 km | FJD 517.50 |

Both are due 11 November 2026 at 23:59 Fiji time. For a clean return, use a meter at or above pickup, fuel 100%, pass each check, add inspection evidence and draw the return acknowledgement signature. Normal configured validation remains in force. The signed rental agreement retains the pickup signature; the return acknowledgement is also stored in the return inspection history.
