# Log – Progress Update Reports
Student Name / ID: Rahul Chand / S11219885  
Course: CS400 — Industry Experience Project  
Semester: 2, 2026  
Project Title: Car and Rental Equipment Management System (CREMS)  
Supervisor: Dr Ravneil Nand  
Report #: 3  
CS400, USP – Log, S22026  
---

## Planned Tasks (Report #3 Period)

• Implement the public customer rental catalogue: asset browsing, availability search, feature display, photo gallery, and booking request submission flow
• Design and develop the Booking Management module for staff: create, view, search, and filter bookings with status tracking
• Implement the Customer Account portal: customer session management, account-linked booking history, and pending booking hand-off
• Develop the Asset Profile and QR label dialogs with full photo gallery management (upload, remove, reorder)
• Implement the Approval Workflow system: approval stages, rules routing, and staff review queues
• Configure professional driver and customer hire preference data model updates
• Set up demo mode configuration and environment build variants (dev, demo, full)

## Completed Tasks

The following tasks were completed collaboratively as part of the project team:

• **Public Customer Rental Catalogue**: Implemented the full customer-facing rental catalogue on `PublicRentalPage.tsx` with division-gated browsing, date/branch/vehicle-type filtering, seat and transmission filters, price-range sorting, live availability checking, and a multi-photo carousel per asset. Asset photos are served through the public API endpoint `api/public/assets/{id}/photos/{fileName}` with response caching for offline browsing support.

• **Customer Account Portal**: Implemented `CustomerPortalPage.tsx` with authenticated customer sessions, booking history display, quotation tracking, and a pending-booking hand-off flow that resumes the rental request after customer sign-in. Customer sign-in/sign-up integrates with email verification using expiring one-time codes.

• **Booking Management Module**: Developed `BookingsPage.tsx` for staff with searchable, filterable booking lists, real-time availability conflict detection in the API layer, and a step-by-step booking creation workflow with charge composition and tax calculation. Booking approval rules were implemented via `BookingApprovalRulesPage.tsx` with configurable approval stages and routing.

• **Asset Profile & QR System**: Implemented `AssetProfileDialog.tsx` with full asset specifications display, the QR label generator (`AssetQrLabelDialog.tsx`) that encodes checkout/check-in URLs, and the asset photo management dialog with drag-free multi-photo upload (up to 8 images), deletion, and catalogue cover selection.

• **Approval Workflow Engine**: Implemented `ApprovalWorkflowService.cs` and `ApprovalWorkflowsController.cs` with support for sequential approval stages, role-based approver assignment, and approval rules that route equipment, personnel, and overtime requests through configurable workflows.

• **Professional Driver & Customer Preferences**: Updated the customer data model to include professional driver details (licence, qualifications) and customer hire preferences (vehicle vs equipment, seat count, transmission type). Database migrations `20260902034110_CustomerPortalAndBookingApprovalRules` and related scripts were applied.

• **Demo Mode & Environment Configuration**: Implemented build-mode-specific configuration with `.env.demo`, `.env.full`, and `.env.example` variants. The `demoMode.ts` configuration module gates page access in demo presentations, and the Vite configuration supports `dev`, `dev:demo`, `dev:full`, `build`, `build:demo`, and `build:full` scripts.

• **Asset Photo Seeding Fix**: Resolved an issue where the `PhotoUrlsJson` field in the asset database records was not being populated for existing data. The `SeedAssetPhotosAsync` method was decoupled from the full development seed in `DatabaseInitializer.cs` and is now called independently on API startup in Development mode, ensuring asset photographs are always synchronised between the filesystem store and the database. The `appsettings.Development.json` was updated with `DevelopmentData:RefreshOnStartup` to force an initial reseed.

## Pending Tasks / Deviations from Plan

• Complete end-to-end testing of the role permission matrix across all division/branch scenarios — pending formal test execution against the staging database.
• Finalise UI refinements for the organisation configuration and division management screens.
• Implement the maintenance job creation and tracking module (in progress for Report #4).
• Some rental inspection checklist questions still require client confirmation (R-009, R-010 in the requirements register).
• The booking handover-to-rental conversion flow is implemented up to the point of quote/booking submission, but the staff-side rental lifecycle (handover → extension → return → closure) requires completion.

## Issues Faced & Resolutions

• **Issue: Asset images not rendering on the public catalogue and staff asset pages.**
  **Resolution**: Investigated the image delivery pipeline — the API `GetAssetPhoto` endpoint validates each photo URL against the database `PhotoUrlsJson` field before serving the file from `App_Data/asset-images/`. The photo-seeding step (`SeedAssetPhotosAsync`) was only invoked when the full development seed ran (empty database or RefreshOnStartup), so databases with pre-existing asset records had an empty `PhotoUrlsJson`. Decoupled `SeedAssetPhotosAsync` from `SeedDevelopmentDataAsync` in `DatabaseInitializer.cs:InitializeAsync` so it runs as a standalone step on every Development-mode startup, loading active assets and synchronising photo URLs with files on disk. Set `DevelopmentData:RefreshOnStartup=true` in `appsettings.Development.json` to populate the URLs on next restart.

• **Issue: Maintaining session consistency across multiple browser windows for both staff and customer users.**
  **Resolution**: Implemented the `WindowSessionMiddleware` at `WindowSessionMiddleware.cs` which enforces a single active browser-window session per user using a fingerprint derived from the session cookie and an `X-CREMS-Window-Id` header. Public resources (`/api/public/*`) and login endpoints are explicitly exempted so image and unauthenticated requests are unaffected. Anonymous public image requests bypass the session check entirely. Staff API requests that lack the session header are rejected with a clear error message, while the React frontend automatically injects the window ID via an Axios request interceptor.

• **Issue: Preventing cross-division data leakage while keeping queries performant.**
  **Resolution**: Existing scoped query filters (refined in Report #2) were extended to cover the new booking and rental pages. Branch-scoped assets are filtered using indexed `BranchId` predicates at the EF Core level. The approval workflow service applies division and branch scopes to approval requests, and the customer portal applies division visibility rules (`IsPublic`) to the public asset API.

## Learning Journey & Technical / Methodological Growth

• **Technical**: Gained deep experience with full-stack image delivery pipelines — from filesystem storage on the API side (`App_Data/asset-images/`), through a path-validated image-serving endpoint with response caching, to React lazy-loaded image carousels with intersection-observer lazy-loading on the frontend. Also learned Vite's proxy configuration for routing API requests during development and the importance of environment-specific configuration (`appsettings.Development.json`, `.env.*` variants).

• **Technical**: Implemented a comprehensive approval workflow engine from the ground up, including domain entity design, EF Core persistence with cascade-save patterns, service-layer authorisation, and REST controller endpoints with policy-based access control.

• **Methodological**: Adopted the "idempotent seed" pattern for development data — designing seed methods that can safely run on every startup without creating duplicates, using dictionary-based lookups and `AnyAsync()` guards. This allowed decoupling photo seeding from the full data seed for safer, more targeted re-seeding.

• **Business Skills**: Strengthened understanding of progressive disclosure in UI design by implementing a dual-portal architecture (staff backend vs customer frontend) where the public rental catalogue uses progressive form disclosure and category filtering to avoid overwhelming individual vehicle-rental customers with unrelated equipment workflows.

## SFIA Generic Skills Self-Reflection

• **Autonomy**: Led the design and implementation of the approval workflow engine and the customer rental catalogue independently, making architectural decisions on API contract design (DTO vs domain entities), image delivery pipeline implementation, and the idempotent seed pattern for development data. Consulted the supervisor only for client requirement clarifications.

• **Influence**: The approval workflow design (sequential stages with role-based approvers) and the public/private portal split architecture will shape how future modules (maintenance, finance, reporting) implement their access control and multi-step business process flows.

• **Complexity**: Managed the technical complexity of coordinating a multi-photograph asset gallery across three layers: filesystem storage on the API, database URL tracking with JSON-serialised lists, and React UI carousels with lazy-loading and error fallbacks. Also handled the complexity of the dual-portal session system where staff and customer sessions use different authentication mechanisms with different timeout and security requirements.

• **Business Skills**: Strengthened requirements documentation skills by mapping client meeting decisions (division capabilities, progressive disclosure) directly to concrete implementation choices in the codebase, and maintained consistency between the requirements register and the implemented feature set.
