# PharmaBill for Windows: rules for the coding agent

## Product
PharmaBill is a pharmacy billing and inventory app for India. Business modes: RETAIL CHEMIST, WHOLESALER, BOTH. It must work offline. It is the Windows counterpart of an existing Android app (Kotlin, Room) and will sync with it. Follow docs/DATA_CONTRACT.md and docs/android-schema/ for table and field names and business rules.

## Technology
- C# on the latest .NET LTS, WPF, MVVM with CommunityToolkit.Mvvm, dependency injection with Microsoft.Extensions.*.
- SQLite with SQLCipher through EF Core. The database key is generated once and stored with Windows DPAPI (ProtectedData, CurrentUser scope).
- QuestPDF for PDFs, ClosedXML for Excel, Serilog for logs, xUnit for tests.
- Nullable reference types enabled. Async everywhere for I/O. No business logic in code-behind or views.

## Solution layout
PharmaBill.Core (entities, enums, business rules, services, no UI or database code)
PharmaBill.Data (EF Core, migrations, repositories, unit of work)
PharmaBill.App (WPF views, view models, themes, resources)
PharmaBill.Sync (sync client)
PharmaBill.Server (later: ASP.NET Core sync server)
PharmaBill.Tests (xUnit)

## Data rules
- Every table has: Id (Guid), CreatedAtUtc, UpdatedAtUtc, DeviceId (origin device), IsDeleted (soft delete only), RowVersion/HlcStamp for sync. Never hard-delete business data.
- Money is `decimal` in C# with 2 decimals. Round half up (MidpointRounding.AwayFromZero) per line, then sum. The sync payload carries integer paise.
- Times are stored in UTC and shown in local time.
- Every stock change writes a StockMovement row in the same transaction. Batch quantity is a cached sum of movements and must always be recomputable from them.
- Bills, invoices, purchases, receipts, register entries and audit logs are append-only. Corrections are new documents (credit note, cancellation with reason).
- All writes go through a unit of work with one transaction. Write an AuditLog row for important actions.

## Business rules (do not weaken)
- Retail sale needs patient name and phone, and address plus prescriber name and registration number for Schedule H1, X and NDPS items. Habit-forming reference drugs (from the catalogue info table) also create a register entry.
- Wholesale sale only to a customer that is Active and has at least one valid, unexpired drug licence of a type allowed for that buyer type. Never to a patient or walk-in. Schedule X and NDPS lines need a matching authorisation on the buyer.
- Never sell expired batches. Block quantity above stock. Warn for near-expiry (configurable). Selling price must not exceed MRP. Batches are picked FEFO.
- Billing is read-only when the drug licence of the active mode has expired, or when the trial or subscription has ended. In read-only mode the user can still view, search, export, print, back up and restore everything, including registers and past bills.
- Statutory register entries cannot be edited or deleted. Retention periods are constants in one config file with a TODO to verify against the current Drugs Rules, 1945 and NDPS rules. Never assume legal values.
- No prices for subscriptions are hard-coded.

## Working style
- Do one task at a time. After changes, build, run the tests, and list the files changed.
- Ask before big refactors or deleting files. Do not add features that were not requested.
- Do not invent legal values, forms or numbers. Put placeholders and TODOs instead.
- Keep UI keyboard-first: Tab order, Enter to move on, shortcuts F2 New bill, F3 Search item, F4 Customer/Patient, F8 Hold, F9 Payment, F10 Save and print, Esc to cancel.