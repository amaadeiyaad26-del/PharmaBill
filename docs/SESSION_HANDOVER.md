# Session handover — 21 Oct 2026

Read this first. It is enough to carry on with no other context.

- Repository: `C:\Users\newth\PharmaBill-MSStore`, branch `ms-store-edition`.
- Remote: `origin` (GitHub `amaadeiyaad26-del/PharmaBill`).
- Pushed up to `94d1f48`.
- Tests: 461, all passing as of `94d1f48`.

## Ground rules for this repo

- **Commit and push only when asked.** `main` is not used for work (see the last section).
- **Never commit `tools/licensing/pharmabill-license-private.pem`.** It is not tracked; only the public key is.
- **Never open, copy or read a real shop database** (`%LocalAppData%\PharmaBill`, or a Store data folder). It holds patient data. Work from test databases.
- **Debug builds use `%LocalAppData%\PharmaBill-Debug`** and must never open the real folder.
- **Schema changes go through EF migrations.** No silent self-repair of the schema at startup.
- **Don't change retail billing's 50/50 CGST/SGST split.**
- **Git stash is shared** with other worktrees. Never run a bare `git stash` / `git stash pop`; use a WIP commit instead.
- **Two untracked items at the repo root are not this session's:** `DELIVERY_SUMMARY.txt` and `LocalPharmacyInstaller/`. Leave them alone.

**Commands:**
- Tests: `dotnet test PharmaBill.slnx -c Release`
- Store package: `.\build-msstore-package.ps1` → `artifacts\msstore\PharmaRemit_<ver>_x64.msixupload`
- Local Market installer: `.\build-installer.ps1` → `artifacts\installer\PharmaRemitLocalMarket_Setup.exe`
- Edition switch: `-p:Edition=Store|LocalMarket` (default LocalMarket), via `PharmaBill.App/Editions/*.props`.
- Store version: set in `Editions/Store.props` and `Package.appxmanifest` (currently 1.0.10 / 1.0.10.0).

## 1. Tonight's fixes — all in `94d1f48` (pushed)

All three landed in one commit, because they touch the same service files.

**Stock is checked inside the save.**
- **Problem:** retail sale, wholesale invoice, stock adjustment and both expiry write-offs checked stock *before* their transaction. A phone sync, an import or another bill landing in between could oversell.
- **Fix:** each now re-reads stock inside the transaction and refuses if short. `Batch.Quantity` is taken from that same read. SQLite runs write transactions one at a time, so that read can't be overtaken.
- **Where:** helper `PharmaBill.Data/Services/StockOnHand.cs`; test `PharmaBill.Tests/StockRaceTests.cs`, which forces a competing write just before the transaction.
- **Already correct, unchanged:** stock transfer, purchase return, replacement issue, stock verification posting.

**Schedule register "Recorded by".**
- **New fields:** `ScheduleRegisterEntry` gains `RecordedByUserId`, `RecordedByName` (a snapshot of the name) and `RecordedBySource`.
- **Stamping:** `RegisterRecordedBy.StampAsync` runs on every path that writes the register: retail, wholesale, purchase receipt, purchase return, both write-offs, correction. Call it **before** any mid-transaction save. Retail allocates receipt numbers with a save, which is why its stamp sits before the payments loop.
- **Shown:** as a "Recorded by" column on the register screen and in its PDF/Excel exports.
- **Migration `20261021162251_RegisterRecordedBy`** fills older entries only where traceable exactly, and never changes any existing field of an entry:
  - `FromBill`: a sale's entry, taken from the bill's `BilledByUserId`.
  - `FromAuditLog`: other entries, taken from the audit-log row written in the same save (same HLC stamp and device, exactly one known user).
  - Everything else stays NULL and reads "Not recorded".
- **Test:** `RegisterRecordedByTests`, which migrates down and up to prove the backfill.

**Phone listeners locked down** (5055 sync station, 5184 purchase-bill scan).
- **Binding:** loopback plus the single *private* shop-network address the QR shows, not every adapter. Rebinds when that address changes. No private address means this PC only.
- **Browsers refused:** no CORS headers. Any request carrying `Origin` or `Sec-Fetch-Site` gets 403; that covers every browser request, WebSocket upgrade and preflight. The Android app (`HttpURLConnection`) sends neither header.
- **Sync page warning:** pair only on the shop's own Wi-Fi, never guest or public Wi-Fi.
- **Where:** `PharmaBill.Sync/LanListener.cs`, `LocalNetworkInfo.GetLanIPv4Address`; test `LanListenerTests`.

## 2. Found tonight, not yet fixed

- **Register screen shows UTC times.** In `MainWindow.xaml`, the statutory register `DataGrid`'s "Date/time" column binds `EntryAtUtc` directly. It needs converting to IST (`ToBusinessLocal`). The PDF/Excel export is already correct (`StatutoryRegistersPageViewModel.ExportFileAsync`). Check the drug-records grid next to it too, which binds `AtUtc`.
- **5184 firewall rule opens the Windows Public profile.** `PurchasePhoneBridge.StartAsync` calls `WindowsFirewallPortOpener.EnsureAllowRule(..., includePublicProfile: true)`. That contradicts the new "own Wi-Fi only" warning. It was left because many shop Wi-Fi networks are wrongly marked Public by Windows. Decide with the owner before changing it.

## 3. Known and accepted — not oversights

- **Phone-made register entries carry no user.** The Android data model (`ScheduleRegisterEntryV20` in the Android project) has no such field. Adding one changes the agreed Android data format, so both apps would need the change. Until then, those entries read "Not recorded".
- **The sync token is exposed on the shop's Wi-Fi.** It is one shared token, sent in plain HTTP on every phone request, so anyone on the same Wi-Fi who captures one request gets full read/write sync access. The origin fix doesn't touch this. The fix is the encrypted per-device link that comes with the terminal work (below).

## 4. Open work, in priority order

1. **Multi-PC terminals.** Design agreed, nothing built.
   - **Model:** one database on a server PC; terminals connect over the shop's LAN. Not several databases that sync. **Never the SQLite file on an SMB/shared folder** (it corrupts).
   - **Server decides:** stock and invoice numbers are decided only on the server. Terminals keep no database. If the server is unreachable, the terminal refuses to bill and says so plainly.
   - **Request IDs:** each bill carries a request ID, so retrying after a lost reply can't bill twice.
   - **Licence:** one licence per shop, bound to the server PC's machine ID exactly as now. Terminals carry no licence: they are authorised by pairing, and the server checks its own licence on every terminal call. Key format and activation stay unchanged.
   - **Pairing:** a 6-digit, single-use, short-lived code on the server screen issues each PC its own revocable key and pins the server's identity. Traffic is encrypted.
   - **Identity:** today every row is stamped with one fixed device ID per install, so on the server every terminal's entry would carry the server's ID. It has to come from the paired terminal on each request.
   - **UI:** lives in Sync Station / the Sync page as a **Devices** panel. Phones stay a separate, clearly labelled group, because they are a synced copy that bills offline. The two models must not converge.
   - **Code reality:** view-models call services that use the local `DbContext` directly. Each screen a terminal uses needs a server call instead; billing counter first.
   - **Listener:** port 5055's existing listener can host terminal calls, but needs per-device keys and an encrypted connection.
2. **Printable recovery sheet for the backup password.** Backups are AES-256 + HMAC-SHA256, keyed from the shop's backup password (PBKDF2-SHA256, 600k rounds). A lost password means an unrecoverable backup. The owner can already reveal the saved password on the Backup page; a sheet to print and keep off the PC is still missing.
3. **HSN-less products billing at 0% GST.** Billing already refuses a product with no HSN or GST rate (`5182664`). The owner reports this as still open, but the remaining gap isn't pinned down. One candidate: `GeminiPurchaseImportService.cs:235` turns a GST it can't read into `0`. That would reach the product as a *recorded* 0%, not a missing one. Verify before fixing, and never treat missing as zero.
4. **Credit and debit notes still read live master data.** Sales and wholesale invoices store seller and buyer snapshots at save time (`SellerSnapshotJson` / `BuyerSnapshotJson`). `ReturnNote` has no snapshot, so a note prints the current shop and customer details.
5. **Branch summaries:** designed in `docs/BRANCH_SUMMARIES.md` (Option A, exported files) and **deliberately not built**.

## 5. Settled decisions — don't reopen

- **Export to Folder replaced Google Drive OAuth backup.**
  - **The button:** one "Back up now" button writes an encrypted `.pbbak` named `<shop> <yyyy-MM-dd HH-mm-ss>` into a folder chosen once; any folder works, including OneDrive, Google Drive or Dropbox.
  - **Removed:** the Google Drive UI (Configure Keys, Link Account, the Drive card, branch network) is gone, and automatic Drive uploads no longer run.
  - **Password:** a backup password is required.
  - **Restore fallbacks:** restore also tries the old derived passwords so earlier USB/Drive backups still open.
- **One licence per shop**, bound to one PC's machine ID.
- **Business dates are always Asia/Kolkata.** Use `PharmaBill.Core.BusinessClock` (injectable; tests pin it) for "today", the financial year and day boundaries.
- **Filed documents record their values at save time,** and reports read those back rather than recomputing from master data.

## 6. What the live Store build is missing

- **Live package:** the version installed from the Store on the owner's PC is **1.0.5.0**. I can't see Partner Center; check there whether a newer submission is pending.
- **Where 1.0.5.0 came from:** an uncommitted working tree on top of `b102d9c`. The 1.0.5 and 1.0.6 version bumps were never committed on their own; the next committed version is 1.0.7 in `aa7446a`.
- **Built but not known to be published:** `artifacts\msstore\PharmaRemit_1.0.10.0_x64.msixupload` was built at `c9157f6`, and everything after it is in no package.
- **Next package:** must be **1.0.11.0 or higher**, built from `94d1f48` or later.

Commits in no published package (oldest first):

| Commit | What |
| --- | --- |
| `aa7446a` | New "pharma REMIT" shield logo; Store 1.0.7, Local Market 1.0.6 |
| `22f5b00` | Dashboard: the shop's name is the heading |
| `3332322` | Dashboard: firm identity as a logo lockup, with logo upload |
| `741ab06` | Google sign-in turned off; local PIN or password only |
| `4ab9fb5` | Seed a missing database from a packaged template |
| `dae9f16` | Startup recovers from empty or unreadable databases |
| `c9157f6` | Store 1.0.10, Local Market 1.0.9 |
| `65e2281` | Every business date decided in IST through one injectable clock |
| `4cb2774` | Store records kept in a user-chosen folder that survives uninstall |
| `44c0107` | Default data folder in the user profile; warn on Google Drive and Dropbox |
| `53d173d` | Stored document paths follow the files when relocating data |
| `b838331` | Back up now to a folder replaces Google Drive account backup |
| `d33692a` | Stale branch stock hints hidden; sales rows dropped from purchase report |
| `c87f6fb` | Cached branch rows dropped from the network sales report |
| `94d1f48` | Stock checked inside the save; register "Recorded by"; listeners locked down |
| (this file) | Session handover |

`4cb2774` (durable Store storage) runs a one-time move of the data on first launch. It was tested end to end in code only: the live Store install shares the package identity, and this PC has no admin rights or Developer Mode. Test the upgrade on a clean VM or spare PC before publishing.

## 7. Waiting on the owner, not on code

- **Drug inspector:** licence-type rules (which licence types authorise which schedules for wholesale buyers).
- **CA:** the B2CL (B2C-large) invoice threshold. `docs/GST_B2CL_THRESHOLD.md` states the source and asks for confirmation.
- **A domain with home, privacy and terms pages**, before anything needs Google OAuth again (Google requires them for verification). Google sign-in and Drive backup are both off until then.

## 8. Housekeeping

- **Flaky test:** `CatalogTests.Importer_HandlesQuotedCommasEmptyCompositionAndCrLf_ThenFtsSearches` is timing-sensitive and fails under load. It passes when re-run alone. Re-run it before treating a failure as real.
- **`main` is far behind:** `origin/main` is at `7b09f6e` (10 Oct) and is **95 commits behind** `ms-store-edition`, with nothing of its own. All current work is on `ms-store-edition`; merging into `main` is an owner decision.
