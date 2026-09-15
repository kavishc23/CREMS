# Inspection browser regression checks

Start the frontend development server on http://localhost:5173.
Run from the repository root with Node and Playwright (Chrome installed).
If Playwright is not in node_modules, set PLAYWRIGHT_MODULE to its absolute package path.

    node tests/browser/inspection-photos-browser.cjs
    node tests/browser/inspection-workflow-browser.cjs
    node tests/browser/asset-inspection-browser.cjs

These tests mount the real React components through Vite. API writes are intercepted:
they verify browser interaction and submitted payloads without changing bookings,
billing customers, or sending emails. They do not verify database persistence.

Coverage includes opening the file chooser, decoded previews, adding/removing files,
corrupt-image feedback, resizing phone photos larger than 3 MB, complete guided
pickup and return submissions, signatures/acknowledgement, and photo evidence in
general asset inspections. Rental inspections are directed to Hire operations.
