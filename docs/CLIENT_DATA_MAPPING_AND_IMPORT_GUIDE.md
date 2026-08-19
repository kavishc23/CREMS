# CREMS client data mapping and import guide

## Status

The supplied Carpenters documents have been analysed and converted into an **anonymised validation package**. Production import is intentionally blocked until Carpenters confirms the mappings, classifications and inspection rules.

## Source inventory

| Source | Records/pages | Result |
|---|---:|---|
| Rental Vehicle Stock List | 39 asset rows, 11 columns | Every column mapped; identifiers anonymised |
| Rental Stock List – CAT | 79 asset rows, 6 columns | Every column mapped; codebook decisions raised |
| Vehicle Rental Inspection Checklist | 4 pages | Pre-hire and post-hire template prepared |
| Equipment Rental Inspection Checklist | 3 pages | Pre-hire and post-hire template prepared |
| Genset Rental Inspection Checklist | 3 pages | Pre-hire and post-hire template prepared |
| Heavy Machine Rental Inspection Checklist | 3 pages | Pre-hire and post-hire template prepared |

## CREMS database alignment

- Core stock fields map to `Asset` (`AssetNumber`, division, service, branch, registration, VIN/chassis, serial, manufacturer, model, year and current meter).
- Flexible fields such as colour, registration expiry, first-registration date, source sort code and capacity/subtype map to configured `AssetAttributeDefinition` and `AssetAttributeValue` records.
- Historical odometer, engine-hour and fuel readings map to `AssetMeterReading`; the current reading remains on `Asset` for fast availability and profile queries.
- Paper checklist questions map to `InspectionTemplate.ChecklistJson` and completed results map to `AssetInspection.ResponsesJson` and `EvidenceJson`.
- Customer, booking, agreement, asset and personnel details should be system-derived rather than retyped into every inspection.
- Terms, collision-damage waiver clauses and excess rules belong to the versioned `RentalAgreement`, not to the checklist template.

## Information protection

The validation workbook and seed package do not contain the supplied registration numbers, VINs, stock numbers or Carptrac equipment numbers. They use deterministic non-production identifiers so the structure can be reviewed without circulating operational identifiers. The raw source files must remain access-controlled and must not be committed to Git.

## Import process after approval

1. Carpenters completes the **Client Decisions** and **Sign-off** sheets.
2. The source owner supplies corrected stock lists and the approved branch, sort-code and make-code reference tables.
3. CREMS performs validation: required values, unique identifiers, valid branches/categories, dates, non-negative meters and duplicate detection.
4. CREMS loads a non-production database inside one transaction.
5. The team reconciles source counts: 39 vehicles, 79 Carptrac assets and 118 total assets.
6. Carpenters verifies a sample from each branch and category, including QR display and pre/post inspection forms.
7. An authorized representative approves the production import window and rollback plan.
8. Production import runs from the corrected, non-anonymised file; raw identifiers are never placed in source control.

## Important unresolved rules

- Canonical asset-number source for Motors and Carptrac.
- Whether Suva Rona Street, Suva Foster Road and Autofield Nakasi are branches, depots or sub-locations.
- Definitions for all Carptrac `Sort` and `Make` codes.
- Whether `Type` represents model, capacity, subtype or mixed data.
- Meaning of `Stocked_Date`.
- Corrections for missing/invalid registration, VIN punctuation and suspicious odometers.
- Safety-critical checklist items, blocking rules, mandatory photographs and maintenance-referral rules.
- Required signatories and inspection-document retention rules.

The full question register is in the validation workbook and should be used as the client review agenda.
