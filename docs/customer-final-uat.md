# Customer preferences UAT

Branch: `final-uat/customer-handover`

## Implemented behaviour

- Account dropdown order: Customer Profile, Rental Preferences, Log out. Mobile navigation also includes Rental Preferences.
- Preferences has its own account view. Profile contains contact details, account security and the driver licence workflow.
- The preferences API reads active, public divisions and their active configured asset categories directly from the database, including divisions that do not yet have registered assets.
- Configuration is loaded each time the page opens, refreshed when its window regains focus, and refreshed every 60 seconds while visible. Refreshing configuration preserves unsaved selections.
- A division checkbox selects its current configured categories. Categories added later appear as unchecked options; selecting Show all rentals includes current and future categories.
- Category IDs are stored per customer. Saving preferences validates that all selected categories still belong to active, public divisions, rejects unavailable IDs, and does not modify contact details.
- Saved categories filter only the homepage division galleries. Categories with the same label in different divisions remain distinct. Full availability searches remain unrestricted by preferences.
- Every homepage division uses a four-slot desktop carousel. A single asset fills the first slot; previous/next controls remain consistent and disable at the ends.
- If selected categories have no listed assets, the homepage shows an explanation and an Edit preferences action rather than showing unrelated rentals.
- Clearing preferences shows all rentals. Existing legacy service preferences remain in the schema for compatibility; the new homepage uses category preferences, which start empty until the customer saves them.

## Automated verification completed

- Website production build: passed.
- Backend build and migration generation: passed.
- Customer journey backend regression suite: 33 passed, including preference persistence, invalid/private category rejection, later configured categories, and contact details remaining intact.
- Homepage preference unit tests: 3 passed (all Carptrac categories, Big bin only, no preference/new categories).
- Browser interaction checks: passed for select/save payloads, account menu navigation, the Carptrac four-card carousel and arrows, Shipping Big bin only, a single card retaining its slot width, searches showing other divisions, saved selections on reopening, new division/category discovery, configuration refresh, and mobile width without horizontal overflow.
- Lint passed for the new preferences page, homepage, shared header, and preference utilities/tests.
- Local API restarted successfully with the migration and served the public catalogue with HTTP 200.
- Browser screenshots are in `tmp/customer-preferences-uat/`.

Backend regression tests use EF InMemory with real Identity services; browser checks intercept API calls with fictional records. These verify controller persistence behaviour and UI integration, but do not replace a client UAT sign-off using real customer login and the live asset register.

## Existing findings outside this change

- Backend build reports existing System.Security.Cryptography.Pkcs package vulnerability warnings; test builds also report an existing SkiaSharp version conflict.
- CustomerPortalPage has three pre-existing lint findings for unused uploadCustomerDocument, quote and BookingCards declarations. This change removed its newly unused preference imports.

## Client UAT checklist

1. Sign in as a customer at `http://localhost:5173`. Open Rental Preferences from the account menu.
2. Compare each displayed division and its categories with the configured organization data.
3. Select all four Carptrac categories, save, and open the homepage. Confirm only Carptrac appears, with four asset cards per desktop row and working arrows when there are more assets.
4. Select only Big bin under Carpenters Shipping, save, and open the homepage. Confirm only Shipping appears and every displayed asset is a Big bin. With one matching asset, confirm it remains in the first slot.
5. Run a full availability search and confirm other divisions/categories remain discoverable.
6. Sign out and back in. Confirm saved category choices remain associated with the customer.
7. Add a public division and active category through configuration, then focus/reopen preferences. Confirm both appear without a code change. Register/publish a matching asset and confirm selecting its category includes it on the homepage.
8. Select Show all rentals and save. Confirm the homepage displays all available division rows.
9. Check the profile, licence, security and mobile navigation before client acceptance.

## Final customer UI fixes — 27 October 2026

- Catalogue cards now use one shared layout with aligned heading, image, price, action and details sections. Visible cards have equal dimensions; long configured names wrap in full and enlarge the shared sections together instead of being clipped. Photos retain their aspect ratio. Single assets retain the first desktop slot.
- Search defaults to today's pickup date and an empty return date. An invalidated return date is cleared rather than silently selected for the customer.
- The different-return-location fields use readable full-width rows. Date controls and location labels are associated accessibly with their inputs.
- Division changes after search now filter the returned results while retaining dates, pickup location and results mode. Availability requests load all divisions for the chosen location/dates, so the results filter can switch divisions without leaving the results view.
- Homepage asset details request no dates and display no date-availability claim, date estimate or booking/quotation continuation. Those controls become available only after a successful dated search and an API-confirmed availability response. Long detail content remains scrollable.
- The profile follows the reference: contact information on the left; compact security and driver licence cards on the right. The permanently reserved scrollbar gutter is removed for customer views, with customer menus and dialogs avoiding unnecessary scroll locking.
- Preferences now uses compact division/category rows, one short explanation, search, Show all rentals, a selection summary and save/homepage actions. New configured categories still refresh automatically.
- Automatic generic write toasts are suppressed for customer workflows and licence processing. Inline feedback reports the actual completed step. Staff mutations retain their existing generic feedback. Account messages clear when changing sections.

Verification: production build passed; all 46 frontend tests passed; both customer browser suites passed. The new suite covers default dates, undated and dated asset details, division result filters, equal card geometry with long future category/asset names, untruncated contents, readable return-location fields, profile positioning, simplified preference saves, and no premature email/licence toasts. The existing preference suite also reconfirmed Carptrac-only and Big-bin-only galleries, arrows, single-card sizing and new configured categories. New/changed components pass lint; the three previously documented unused declarations in CustomerPortalPage remain pre-existing findings.

Browser checks ran against the user's existing frontend with fictional intercepted API responses; they did not modify live customer or asset records. No frontend or backend server was started or restarted for these fixes. Screenshots: `tmp/customer-ui-final-uat/` and `tmp/customer-preferences-uat/`. Client acceptance against the real configured register is still required.

## Final customer website UI testing — 27 October 2026

Branch: `final-customer-website-ui-testing`.

- Customer page headers now use the shared CREMS page header. Account trigger has a visible dropdown arrow, and menu spacing is compact.
- Rental preferences use separate division cards and selectable category chips, with configuration-driven categories and existing persistence/homepage filtering retained.
- Asset fulfilment supports Pickup only, Delivery only, and Pickup or delivery. API validation enforces these choices. Existing delivery flags are migrated to Delivery only.
- Booking/quotation checkout has three steps: rental and dates; customer details and configured service requirements; price and submit. Profile identity and contact details are read-only, including server-side phone authority. Secondary contact is optional.
- Hourly hire edits update the estimate immediately. Delivery or personnel requests always require quotation; their charges are deferred for staff confirmation and excluded from the known hire estimate.
- Search results occupy a temporary history entry. Leaving requires confirmation, cancelling preserves the search, and history navigation does not restore discarded results.
- Rental summary focuses on hire details, issued invoice/balance, bond where relevant, and downloadable documents. Invoice PDF text wraps and paginates.

Verification:
- Production frontend build passed; 46 frontend unit tests passed.
- 47 focused backend quotation and customer journey tests passed using isolated CustomerUat configuration.
- Existing browser UI regression suite passed with intercepted fictional API data against the already-running frontend.
- Added checkout/navigation browser coverage for three steps, read-only identity fields, live hourly estimates, delivery quotation switching, Back confirmation, stale-result disposal, and compact summary/document downloads.
- Remaining CustomerPortalPage lint findings (`uploadCustomerDocument`, `quote`, `BookingCards`) predate these changes.

Runtime handover: No frontend/backend server was started or restarted. Restart the backend yourself in VS Code to load the API changes and apply the new AssetCustomerFulfilmentOptions migration through the existing startup database initializer. The currently running API has not loaded this migration or these new API rules. Browser regression API responses were intercepted, so those tests do not claim live-database integration verification.

### Follow-up corrections — customer request forms

- Removed the incorrect non-vehicle quotation restriction. Configured booking-only and booking/quote services now expose booking for projectors and other eligible assets. Booking-only service choices override division quote defaults; delivery and personnel still use quotation for charges awaiting confirmation.
- Restored explicit confirmation before changing a pickup booking to delivery quotation, with a Keep pickup booking alternative.
- Reset request dialog state when opening/closing requests and ignore same-results history events in the navigation guard. Added browser coverage reopening forms without an exit prompt.
- Condensed request dialogs, stepper, fields and price summaries. Purpose and Additional message share a row; delivery address appears only for delivery. Removed repeated delivery explanations and redundant quotation-required labels.
- New booking/agreement references have eight-character suffixes with existing-reference collision checks; new public quotation references use the same year/suffix shape. Existing stored references are retained, with compact legacy summary labels and full hover text.
- Clarified existing service description field: it describes the service and does not control request routing. It predates this follow-up and the preceding customer UI commit.
- Verification: production frontend build and 46 unit tests passed; focused 49 backend customer journey, quotation, booking and reference tests passed. Browser checks use fictional intercepted data against the existing frontend; include both delivery-confirmation outcomes and no-scroll rental/details steps at 1366×768.
- An expanded settings test run exposed a pre-existing stale assertion expecting seven settings where the controller returns twelve; no production settings change was made for it.
- No application server was started/restarted. Reload backend changes through the user's VS Code process before live testing.

### Adaptive forms, customer notifications and branded financial documents

- Removed the fixed 90% customer-site zoom. Shared layout defaults now keep containers, grids, dialogs and tables responsive without shrinking the entire interface or hiding horizontal overflow. Request dialogs use responsive spacing and a bounded readable width; fields and footer remain reachable on smaller screens.
- Rebalanced booking/quotation forms with comfortable field spacing and side-by-side Purpose / Additional message. Delivery address remains conditional. Driver/operator hours are no longer collected or used as a quotation assumption; staff confirm arrangements and charges. Configured service requirements and hourly asset hire quantities remain functional.
- Simplified delivery conversion confirmation to Keep pickup / Get quotation. Estimates show base hire, VAT, one clear hire estimate, pending delivery/driver-operator items and a bond only when relevant.
- Standardised inclusion wording as Driver/operator included across customer forms, asset details, asset configuration and division policies.
- Created a separate customer-only notification panel: latest five active updates, unread badge, mark all read, and My bookings. Staff notification filters, priorities, preferences, compose tools and history remain in the admin component. Customer polling no longer uses the admin long-poll/toast path; related customer navigation goes through the existing exit guard.
- Replaced customer invoice/quotation plain-text PDF exporters with a shared CREMS black/yellow logo template, item table, labelled metadata, highlighted totals, repeated page headers and numbered footers. Staff invoice summaries use the same branded template; recorded amounts are retained. Original uploaded documents were read-only references.
- Verified: production build and focused lint passed; 46 frontend unit tests and 49 focused backend customer/quotation tests passed. Browser regression passed against the existing frontend with intercepted fictional API data, including 1366×768 forms, mobile/tablet widths, simplified confirmation, conditional fields, five-item notifications/read state, invoice downloads and quotation export. Branded invoice and quotation PDFs were rendered and inspected; a five-page invoice stress test confirmed long descriptions, continued tables and totals paginate.
- No frontend/backend service was started or restarted. Backend request-note/operator-hour behavior takes effect after the user reloads the backend in VS Code. Existing framework/package warnings remain outside this UI change.

### Licence-class OCR verification — 27 October 2026

Scope is limited to local OCR implementation and backend tests. No frontend/UI, booking, preference, notification, report or database schema changes.

- Added a Fiji-card class reader that detects the blue card within a photograph, locates the printed class label, and reads the value below it. It uses complementary colour/contrast passes, isolated digit/comma recognition and repeated reads.
- Tiny omitted commas in an isolated value such as 23 are normalized to classes 2 and 3. Low-confidence guesses and contradictory ties are rejected. The value area is separated from Conditions, dates and licence-number fields.
- The class pass uses its own OCR engine to avoid adaptive-state contamination of the existing name/number OCR passes. Existing parser fallback, blur validation, identity/name matching, confirmation, storage and manual handling remain in place. Existing PDF-first-page rendering remains unchanged.
- No model data was retrained or sample identity hard-coded. Personal source documents and temporary OCR output were excluded from the repository and no documents were sent to an OCR service during evaluation.
- Reproduced baseline failures: Jayshil JPG had no classes; Rahul JPG returned only class 3. Updated class reader returns 2 and 2,3 respectively. Kavish JPG returns 2; the smaller Anonymous PNG returns 2. The supplied PDFs retain expected 2 and 2,3 through targeted reading or the existing fallback.
- Limitation: the low-resolution Anonymous (2) PNG does not yield a reliable class-label read and its original full OCR also fails an identity-field read. It remains an unreadable-scan case instead of manufacturing a class result; a clearer source is required for that sample.
- The isolated class pass took roughly 2–3 seconds per supplied input during final evaluation. Existing full-document passes still contribute to total upload-scan time.
- Verification: 100 focused backend tests passed, including OCR parsing, class consensus, photographed-card location, blur rejection, confirmation rules and existing booking/quotation journeys. The test project now explicitly matches the API's existing SkiaSharp 4.153.1 dependency, resolving the test-only image-assembly mismatch.
- No application servers were started or restarted. Reload the backend in VS Code to use the updated OCR implementation.

## Minor laptop layout polish — 27 October 2026
- Scoped changes to customer profile and booking/quotation request dialog only.
- Moved the active step heading above both columns to align selected asset and estimate cards.
- Reduced vertical spacing at laptop heights; corrected profile minimum-height assumptions without clipping content or disabling necessary scrolling.
- Frontend production build passed. Customer browser regression suite passed against existing frontend with fictional mocked APIs; delivery plus required driver/operator card alignment and no-scroll layout, and profile no-scroll layout checked at 1280x720, 1366x768 and 1536x864. Screenshots visually reviewed. No servers started or restarted.

## Action feedback, quote acceptance and conditional licence details — 29 October 2026

- Replaced transient completed-action banners across customer and administrator pages with a shared action notice hook. CREMS toasts use compact white/yellow styling and expire after three seconds even while hovered. Required validation guidance and stored record status remain available.
- Customer quote acceptance now blocks duplicate submissions, closes the quotation dialog on success, refreshes the journey, and displays Awaiting review until the linked booking is confirmed by staff. A failed list refresh cannot leave the old acceptance action available.
- Removed internal quotation version labels from customer journey cards, quote dialogs and customer quote PDF details. Version checks remain in decision requests to protect against accepting a stale price.
- Checkout shows the verified, read-only licence number and classes only when the configured division rule requires a licence for a customer-driven vehicle. Requests for equipment, included driver/operator service, or disabled licence policies omit driver details; the API independently enforces this. Applicable booking notes and quotation enquiries include the authoritative saved licence classes for staff review.
- Verification: production frontend build and 46 frontend tests passed; 53 focused backend tests passed, including quote acceptance remaining Draft for staff confirmation and licence omission rules. Existing broad customer browser suite and new customer/admin action-and-licence browser checks passed using fictional mocked API data against the existing frontend. Visually reviewed profile toast, licence form and admin toast screenshots. No application servers started or restarted.

## Customer rental stages and summary — 29 October 2026

- Unified each linked quotation and booking into one customer journey. Draft and revised unsent offers remain hidden until sent by staff. Quote acceptance remains subject to staff confirmation.
- Booking stages: Awaiting Confirmation (grey), Confirmed (green), On Hire (orange), Completed (blue). Quotation requests begin with Awaiting Quotation and Review Quotation (grey), then follow the same booking stages. Expired, Declined and Cancelled use red.
- Actions follow each stage: direct cancellation with confirmation before approval; reasoned cancellation and extension requests after confirmation; incident reporting during hire. Reviewing a sent offer provides download, acceptance and reasoned decline. Customer decisions and cancellations enforce ownership, current stage and version on the API.
- View summary includes available quotation, signed agreement and invoice, charges/payments, incidents, inspection damage, request outcomes and customer notification history. Existing customer and division licence rules are respected.
- Added deduplicated customer pickup-day and return-day notifications using Fiji calendar dates, plus a thank-you message when staff complete the return. The worker must be running; no schema migration is required.
- Verification: 59 frontend tests, 59 focused backend tests, TypeScript and production frontend build passed. Dedicated browser checks cover the complete stage/action matrix, cancellation, acceptance, documents, activity and mobile width. Existing customer forms/notifications and action/licence regressions also checked with fictional intercepted API data. No application servers started or restarted.
