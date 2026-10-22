# Session handover — 22 Oct 2026

Read this first. It replaces the 21 Oct handover and is enough to carry on with no other context.

- Repository: `C:\Users\newth\PharmaBill-MSStore`, branch `ms-store-edition`, remote `origin` (GitHub `amaadeiyaad26-del/PharmaBill`).
- Pushed up to and including the commit that adds this file.
- Tests: 491, all passing at `5b8589a`.

## Ground rules for this repo

- **A feature is not done until someone has used it through the UI.** Tests that call the service directly prove the logic, not the feature.
  - **Why this rule exists:** the folder backup passed 444 tests and had never made a single backup, because no test pressed the button. The password boxes never passed what was typed to the page, so every backup was refused.
  - **What counts:** drive the real view-model through the real controls (type into the box, press the command, check what the screen shows), or use it in the running app.
  - **Pattern to copy:** `BackupPageEndToEndTests`.
- **Commit and push only when asked.**
- **Never commit `tools/licensing/pharmabill-license-private.pem`.** It is not tracked; only the public key is.
- **Never open, copy or read a real shop database** (`%LocalAppData%\PharmaBill`, or a Store data folder). It holds patient data. Work from test databases.
- **Debug builds use `%LocalAppData%\PharmaBill-Debug`** and must never open the real folder.
- **Schema changes go through EF migrations.** No silent self-repair at startup. The latest migration is `IndexCustomCatalogMedicines`, and `PersistenceTests.DbInitializer_AppliesInitialMigration` asserts the newest one's name: update it when you add a migration.
- **Don't change retail billing's 50/50 CGST/SGST split.**
- **Git stash is shared** with other worktrees. Never run a bare `git stash` / `git stash pop`; use a WIP commit.
- **Not this project's work:** `DELIVERY_SUMMARY.txt` and `LocalPharmacyInstaller/` at the repo root are untracked and belong to someone else. Leave them alone.

**Commands:**
- Tests: `dotnet test PharmaBill.slnx -c Release`
- Store package: `.\build-msstore-package.ps1 [-Version 1.0.12.0]` → `artifacts\msstore\`. Add `-CertPath` / `-CertPassword` to sign for sideloading only; never upload a signed package.
- Local Market installer: `.\build-installer.ps1`
- Edition switch: `-p:Edition=Store|LocalMarket` (default LocalMarket).

## 1. Billing terminals — steps 1–3 built and committed

| Commit | What |
| --- | --- |
| `4dea6e7` | Step 1: the billing-counter server (HTTPS on 5056, key per device, sessions, counter operations, request IDs) |
| `347ea32` | Changing a batch MRP needs stock-entry permission everywhere (service-level check, all screens and terminals) |
| `7aeff5f` | Void and return a bill at the counter (admin PIN for void; admin unlock for bills past the 5-day lock; retry-safe returns) |
| `a66ce81` | Sessions survive a server restart (12 h from sign-in, 2 h idle); the server's user record decides every call |
| `d560b9b` | Step 2: pairing with a short code, the Devices panel, mDNS discovery |
| `327d3d5` | Records made from a terminal carry its device ID and name (audit log, Schedule H register "Machine" column) |
| `60c21c2` | The terminal's client: pinned TLS, failures turned into things a cashier can act on, retries with the same request ID |
| `5b8589a` | Step 3: counter mode (terminal window, first-run choice, offline handling); also fixes the desktop "next bill" preview |

**Where the code is:**
- Server, protocol, pairing, discovery, client: `PharmaBill.Sync/Terminals/`.
- Terminal UI: `PharmaBill.App/TerminalWindow.xaml`, `ViewModels/TerminalViewModel.cs`, `WelcomeWindow.xaml`, `ViewModels/TerminalPairingViewModel.cs`.
- Devices panel: `Views/DevicesView.xaml`, `ViewModels/DevicesViewModel.cs`, on the Sync Station page.
- Per-request identity: `PharmaBill.Data/Persistence/RequestDevice.cs`.
- Tests: `TerminalServerTests`, `TerminalPairingTests`, `TerminalSessionsTests`, `StockRaceTests`.

**Design — settled, do not reopen:**
- **One database, on the server PC.** Terminals connect over the shop's LAN and hold no records. **Never SQLite on an SMB/shared folder** (it corrupts after weeks).
- **The server decides** stock, invoice numbers, prices against MRP and permissions, inside its own write transaction. Every save, return and void carries a request ID, so a retry after a lost reply never bills or credits twice.
- **An unreachable server stops billing.** The terminal refuses and says so plainly. It never bills into a local copy that reconciles later.
- **Licence:** one per shop, bound to the server PC's machine ID as before. Terminals have no licence of their own; they are authorised by pairing, and the server checks its own licence on every call. A paired PC starts in counter mode before any data folder, database or licence is touched.
- **Pairing:** a 6-character code (no look-alike characters) on the server's screen, valid 5 minutes, used once, cancelled after 3 wrong tries.
  - **The code never crosses the network.** Both ends prove they know it, and the proofs are bound to the server certificate the terminal actually sees, so a relaying machine in the middle fails.
  - **What the terminal keeps:** the pinned certificate and its own revocable key, DPAPI-protected.
- **Identity on records:** each terminal request runs under the terminal's identity, so audit rows and register entries name the machine. The user comes from the server's records, re-read on every call.
- **Phones are a separate group** ("Phones (synced copies)"): they keep their own copy, bill offline and sync. **Phones and terminals must not converge.**
- **Off by default:** owners and managers switch serving on in Sync Station > Devices. A PC is a server or a counter, never both.

## 2. Not yet tested on real hardware — two PCs on a real LAN

**Checklist:** `C:\Users\Public\PharmaRemitTerminalTest\CHECKLIST.txt`. About 20 steps with what correct looks like at each. Both PCs need the same build; an installer from `5b8589a` or later has not been made yet.

**What it settles** (none of this can be checked on one PC):
- whether mDNS discovery crosses the shop's router (client isolation or multicast filtering would block it; typing the address still works);
- the Windows firewall prompts on both machines;
- the server PC fully restarting while counters are signed in;
- real printers on the counter.

**What has been verified:**
- the whole flow against a real HTTPS server in tests, including a relaying machine in the middle;
- **through the real windows** (`TerminalScreenTests`, typed into and pressed, against a real server on loopback):
  - pairing: the code from the Devices panel's "Add a billing counter", typed into the Welcome window (wrong code refused, right code pairs, the panel lists the counter);
  - sign-in with the PIN typed into the PIN box (wrong PIN refused, right PIN signs in, box cleared, sign out);
  - a Schedule H1 bill without a prescription, billed only after the admin PIN is typed into the override box;
  - return and void from the Recent bills tab (admin PIN box for the void; a cashier's return refused).
  - Before 22 Oct the PIN boxes never passed what was typed (the `PasswordBoxBinding` fault), so none of this worked for a person. Putting that fault back makes these tests fail.
- discovery on this PC's real Wi-Fi adapter (192.168.1.20): advertised and found, with the fingerprint matching.

**Still not driven through the UI:** "Find the server" (mDNS; needs the real network), a save whose reply is lost then "Confirm with server" (needs the server to vanish mid-save; covered at client level), attaching a prescription file (opens a file dialog), "Stop using this PC as a counter" (message box), printing to a real printer.

**Will catch people:** the server PC's network must be set to **Private** in Windows. The firewall rule for 5056 is private/domain only, by design, so on a "Public" network counters cannot connect. The Devices panel does not warn about this yet.

## 3. Not yet tested — the Store data-folder migration on a clean account

- **Kit:** `C:\Users\Public\PharmaRemitMigrationTest`:
  - `CHECKLIST.txt`;
  - `PharmaRemitMigrationCheck.exe` (stages `before` / `after-upgrade` / `after-reinstall`; it refuses to run in the `newth` account, against any shop not named "Migration Test Pharmacy", or with the app open);
  - `PharmaRemit_1.0.11.0_x64.msix` and the test certificate `.cer`.
- **That package is from `edac683`, before the terminal work.** It is fine for testing the data move. The package actually published must be rebuilt from the current head.
- **The tool's source** is in `tools/store-migration-check/` (see its README). `run-selftest.ps1` proves it before use: a clean upgrade must pass and five injected failures must be caught.
- **Risk:** Windows may refuse a self-signed update over a Store-signed install (step B2). If so, that is a sideloading limit, not the customer path. Use a **private Partner Center package flight** of the next version to the test account's Microsoft account.
- **Reinstall from the file, not the Store:** reinstalling from the Store gives back 1.0.5.0, which knows nothing of the data folder and would open an empty shop.
- **Clean-up afterwards:**
  - remove the certificate from `Cert:\LocalMachine\TrustedPeople` on the test account (thumbprint `975920B684AD2ACB42AAA565407010B762A41AB5`);
  - remove the signing certificate with the same thumbprint from `newth`'s `Cert:\CurrentUser\My`.

## 4. Not in the first-pass counter

- **Missing features:** split payments, loyalty points, credit/ledger sales, quotations, substitutes.
- **Not looked at visually:** `WelcomeWindow`, the first-run "main PC or billing counter" choice. The other screens were checked.

## 5. Everything else open, in priority order

1. **Printable recovery sheet for the backup password.** Backups are AES-256 + HMAC-SHA256 from the shop's backup password (PBKDF2-SHA256, 600k rounds). A lost password means unrecoverable backups. The owner can reveal the saved password on the Backup page; a sheet to print and keep off the PC is still missing.
2. **HSN-less products billing at 0% GST.** Billing has refused products with no HSN or GST rate since `5182664`, so check this first: `GeminiPurchaseImportService.cs:235` turns a GST it can't read into `0`. That would land on the product as a *recorded* 0%, not a missing one. Never treat missing as zero.
3. **Credit and debit notes read live master data.** `ReturnNote` has no seller/buyer snapshot, unlike sales and wholesale invoices (`SellerSnapshotJson` / `BuyerSnapshotJson`), so a note prints today's shop and customer details.
4. **The register screen shows UTC, not IST.** The statutory register `DataGrid` in `MainWindow.xaml` binds `EntryAtUtc` directly; the drug-records grid next to it binds `AtUtc`. Exports are correct. `Controls/BusinessLocalTimeConverter.cs` (added for the counter) does the conversion.
5. **The 5184 firewall rule still opens the Windows Public profile.** `PurchasePhoneBridge` uses `includePublicProfile: true`, which contradicts the "own Wi-Fi only" warning on the Sync page. Decide with the owner.
6. **Branch summaries** are designed in `docs/BRANCH_SUMMARIES.md` (Option A, exported files) and **deliberately not built**.

Also stale: `MSSTORE_SUBMISSION_GUIDE.md` lines 44 and 69 still describe and justify `unvirtualizedResources`. The package does not declare it (see section 7). Fix the guide so nobody mentions it to certification.

## 6. Settled decisions — don't reopen

- **Export to Folder replaced Google Drive OAuth backup.** One "Back up now" to a chosen folder (OneDrive/Google Drive/Dropbox folders are fine). Encrypted `.pbbak`, a backup password is required, and restore also tries the old derived passwords.
- **One licence per shop.**
- **No AI or online medicine lookup.** "Search data bank" (Add Medicine, and "Not found - add manually" on billing, purchase and the global search) fills details from the shop's own medicines and the built-in data bank only, offline. Gemini/OpenRouter/OpenFDA lookup was removed on 22 Oct at the owner's request. Gemini is still offered for reading purchase invoices and prescriptions; that was not part of the request.
- **Business dates are always Asia/Kolkata** (`PharmaBill.Core.BusinessClock`, injectable; tests pin it).
- **Filed documents record their values at save time;** reports read those back rather than recomputing.
- **Returns stay with pharmacists, managers and owners.** Cashiers don't get them: a registered pharmacist must be present during trading.
- **MRP correction needs stock-entry permission everywhere.**

## 7. Publishing

- **All of this ships in one update.**
- **Capabilities:** the 1.0.11 manifest declares only `runFullTrust`. The build script refuses any package requesting `unvirtualizedResources` or disabling write virtualisation.
- **Version history:**
  - **Live in the Store:** 1.0.5.0, which predates all of this work.
  - **Last upload:** 1.0.10 was uploaded to Partner Center with the restricted-capability warning.
  - **Next package:** must be 1.0.11 or higher, built from the current head. If the clean-account test sideloads 1.0.11, publish as 1.0.12 so that machine still takes the Store update.
- **Before publishing,** the two hardware tests in sections 2 and 3 should pass.

## 8. Waiting on the owner, not on code

- **Drug inspector:** licence-type rules for wholesale buyers, and **whether returned medicines may go back into saleable stock** (returns currently restock by default).
- **CA:** the B2CL invoice threshold (`docs/GST_B2CL_THRESHOLD.md`).
- **A domain with home, privacy and terms pages** before anything needs Google OAuth again. Google sign-in and Drive backup are off.

## 9. Housekeeping

- **No known flaky test.** `CatalogTests` used to time one cold search against 200 ms and failed under load. It now checks that the search is answered by the full-text index (and notices when it isn't). Speed is measured warm on 100,032 medicines in `CatalogSearchSpeedTests`.
- **Search speed:** every search also looked for the shop's own (custom) medicines by reading the whole catalogue, about 450 ms on 100k rows and about 1 s on the real 254k. The partial index `IX_CatalogMedicines_IsCustom` brings a warm search to about 30 ms.
- **Pooled connections:** opening the encrypted database costs about 0.35 s (SQLCipher key derivation). Microsoft.Data.Sqlite drops connections idle through two prune cycles (140 s each), so `DatabaseKeepWarmService` touches the pool every minute. It never opens the database while `DatabaseMaintenance` is held (restore, fresh store) and never creates a missing one.
- **`main` is far behind:** `origin/main` is **104 commits behind** `ms-store-edition`, with nothing of its own. Merging is an owner decision.
