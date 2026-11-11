# Vehicle and events catalogue extension

Run from the repository root:

```sh
./.dotnet/dotnet run --project ops/ExtendRentalCatalogue
./.dotnet/dotnet run --project ops/ExtendRentalCatalogue -- --apply
```

The first command inspects configuration. `--apply` adds records to the local
`Crems` database in a transaction. The utility suppresses notifications and does
not add bookings, customers, invoices or payments. It matches existing category,
service and asset identifiers so repeat runs do not create duplicates.

Carpenters Motors receives Sedan, Hatchback, SUV, Pickup and Minivan categories,
linked to its existing Vehicle rental service. Eight assets use the `CM-` asset
tags, across Suva and Nadi. These tags are internal identifiers, not asserted
vehicle registration numbers. Categories include seat/transmission attributes
and pickup/return inspection templates.

Carpenters Events receives Projector hiring, a Projector category and
`CE-PJ-001`, an Epson EB-X49. The Suva branch is linked to the new division and
service. The projector tracks lamp hours, excludes fuel checks and does not
require a driving licence. Its checklist covers the lens, casing, image output,
cables, remote, case and lamp reading.

Rental rates, asset values and meter readings are editable catalogue configuration
amounts. Confirm them against actual fleet records and approved tariffs before
using them for live hires. Tax follows the existing Motors division setting.
Existing assets and historical transaction records are retained.

Projector specifications and catalogue photograph:

- [Epson EB-X49 product details](https://www.epson.co.uk/en_GB/products/projector/portable/eb-x49-projector/p/29758)
- [Epson product photograph](https://cdn-eu.aglty.io/epson/productimages/-productpicture-hires-eb-2042_4.jpg)

## Saving pickup and return work

**Save progress** stores the complete form and current step in this browser's
IndexedDB. Checklist results, notes, readings, photographs and drawn signatures
are restored when the workflow reopens on the same device/browser. Pickup and
return drafts have separate booking-specific keys. The button accepts incomplete
checklists and does not release an asset, post a payment or complete a return.

**Save pre-hire inspection** remains a separate server-side inspection action.
Completion actions still enforce the required checks, signatures and financial
validation. Clearing browser storage removes local progress; these local drafts
are not shared between different computers.
