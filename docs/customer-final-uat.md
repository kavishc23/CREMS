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
