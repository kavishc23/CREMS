# CREMS workflow readiness

## Implemented and testable

- Customer registration, activation, verification and authentication.
- Public Rentals and Carptrac catalogue, date availability and authenticated requests.
- Thirty-minute pending-request holds, overlap protection and operational asset validation.
- Customer cancellation, extension and active-hire incident requests.
- Division- and branch-scoped staff queues and asset access.
- Booking review, pricing, confirmation and cancellation state rules.
- Driver, payment, agreement, handover inspection, QR checkout, return inspection and invoicing.
- Damage-to-inspection routing and maintenance work logging.
- Equipment quote, approval, dispatch, collection and trained-personnel configuration foundations.
- Customer booking progress and staff operational dashboards.
- Automated booking policy tests.

## Client decisions required before production configuration

- Whether customers reserve an exact asset or an asset category.
- Which vehicle requests may be instantly confirmed.
- Draft hold duration and cancellation/no-show rules.
- Actual rates, VAT handling, deposits, damage waivers and approval limits.
- Equipment transport zones, operator rates, qualifications and scheduling ownership.
- Included kilometres/engine hours and excess usage calculation.
- Official agreement, inspection, quote and invoice wording.
- Accounting/payment integrations and corporate credit approval.
- Production email domain, file storage, hosting, backup and retention requirements.

## Production gate

Production release still requires client sign-off, data cleansing/import, full integration and security testing,
backup/restore testing, monitoring, user training and user-acceptance testing with each role and division.
