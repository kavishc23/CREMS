# Fiji rental pricing defaults

Configured on 24 September 2026 for new quotes/bookings. All rates remain editable by authorized staff; historical bookings, agreements, payments and invoices are not repriced.

## VAT

Standard VAT: **12.5%**, effective 1 August 2025. The existing application treats listed daily hire rates as VAT-exclusive. Refundable bonds remain separate from taxable hire charges.

Source: [FRCS VAT guide](https://frcs.org.fj/our-services/taxation-section/non-individuals/reporting-and-paying-taxes/vat-guide/) and [FRCS calculation notice](https://frcs.org.fj/public-notice/value-added-tax-vat-calculation-notice/).

## Refundable bonds (FJD)

| Category | Starter bond | Basis |
| --- | ---: | --- |
| Sedans, hatchbacks, wagons | 1,000 | Published Fiji operator benchmark |
| SUVs, vans/minibuses, utes/4WD | 2,000 | Published benchmark; applied to the NV350 16-seater |
| Coaches, cargo trucks | 3,000 | Business estimate for larger commercial vehicles |
| Heavy machinery | 5,000 | Business estimate |
| Forklifts, generators | 2,000 | Business estimate |
| Scaffolding | 1,000 | Business estimate |
| Light equipment | 500 | Business estimate |
| Portable toilets, bins | 300 | Business estimate |

[Aries Rental terms, clauses 35–36](https://ariesrental.com/terms-and-conditions/) publish 1,000 for small cars and 2,000 for SUVs/minivans/utes/4WDs. These are market examples, not Carpenters' published policy or statutory bond amounts. [Deo Hire Fiji](https://www.deohirefiji.com/faq) requires security deposits for new customers but does not publish fixed amounts. Equipment and larger commercial vehicle amounts above are editable starter business assumptions, not verified market averages or insurance excess limits.

## Applying existing-catalogue configuration

Run `ops/configure-fiji-rental-pricing.sql` once against the intended CREMS database. It updates the MOTORS, CARPTRAC and SHIPPING rental divisions, their known services, and their zero-bond assets. It preserves nonzero asset/service bond overrides and writes a completion marker so a later run cannot undo a deliberate waiver. Other custom/test divisions are excluded. Take a configuration snapshot before applying.

Future seeded assets receive the same suggested bonds. Manually created assets retain explicit staff configuration. Neither the helper nor the SQL forces a nonzero bond over an intentional waiver after setup.

Example: Nissan NV350 one-day base hire 240 + VAT 30 + refundable bond 2,000 = **2,270 FJD** including bond; hire total is **270 FJD**.
