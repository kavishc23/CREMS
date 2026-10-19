# Configurable hire pricing and housing

## Setup

1. In Organization, create an active division, service and asset category. Enable the division and service under Branch operations for the intended branch, including online booking when required.
2. In Asset register, select the division, service, category and branch. For housing, choose **Property / housing unit** as the asset kind. This removes driving-licence, operator, fuel and odometer requirements.
3. Choose the price source: **This asset's rate**, or **Base-hire charge from Rates & charges**.
4. For an asset rate, choose the billing unit and amount. Units are hour, day, week, month, unit, trip, kilometre, tonne, square metre and fixed.
5. For a service rate, configure exactly one active BaseHire charge for that service. A service-specific charge takes precedence over a division-wide charge. Missing or ambiguous charges are rejected with an actionable message.
6. Set the bond in the division/service or override it on the asset. Online visibility also depends on branch/service activation and the asset being operational.

## Billing behavior

Time-based quantities round up started hours, days, weeks or calendar months. A calendar month follows the start-date anniversary, clamping to the last day where necessary (31 January–29 February 2028 is one month). Measured units require a quantity; fixed charges use one.

New bookings save their rate, unit, quantity and tax treatment. Later changes to asset/service rates do not rewrite agreed booking prices. Quotes preserve their base-hire unit and quantity when converted; agreements and invoices use the saved booking values. Late-return fees use the agreed hire amount divided by scheduled days, then the configured late-fee multiplier.

Property units use a premises inspection checklist and handover/return workflow. Monthly billing here means a rate for the booked period, not automatic recurring invoice generation.

The additive ConfigurableHirePricing migration preserves existing assets/bookings as daily hire and existing bookings as taxable. Existing daily quantities remain calculated from their original dates.

## Verification

HirePricingTests covers calendar boundaries, quantities, legacy pricing, service/division precedence, ambiguous configuration and property policy. ReturnChargesAndBondTests includes a monthly tax-exempt property return after the asset rate changes. The browser fixture verifies configured monthly pricing in the property detail view. RentalSettingsSqlIntegrationTests applies migrations to a temporary SQL database and verifies the seven saved system settings through authenticated HTTP.

Browser fixtures use mocked data and do not change live settings. Runtime deployment and full live tenant journeys must be distinguished from these checks.

## Verified on 20 October 2026

- Backend: 337 passed, 2 opt-in SQL tests skipped in the normal suite; no failures.
- Isolated settings SQL/HTTP integration: passed separately, including applying the new migration.
- Frontend: 25 tests passed; production build and TypeScript checks passed.
- Property detail browser fixture: passed for configured FJD 900 monthly pricing over the leap-year month boundary.
- Corrected the profitability CSV regression test to locate numeric columns by their headers.
- Applied the additive migration to the development database and restarted the local API with the PricingWork build. Read-only catalogue check returned 18 assets with the new pricing fields.
- Existing broader frontend lint errors remain in these pages; the lint check is not clean. Full live tenant quote-to-return browser coverage has not been performed.
- Changes remain local and uncommitted.
