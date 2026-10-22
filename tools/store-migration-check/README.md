# Store data-folder migration checker

Checks, on a clean Windows account, that a Store update moves a shop's records into the chosen data
folder with nothing lost. Run it at every release that touches storage, startup or migrations.

## What it checks

`PharmaRemitMigrationCheck.exe` runs at three points, with Pharma Remit closed:

| Stage | When | What must hold |
| --- | --- | --- |
| `before` | Live Store build installed, test shop set up | Records the shop: bills, totals, stock per batch, register entries, attachments, every file's hash |
| `after-upgrade` | New package installed and the data folder chosen | Every record identical in the chosen folder. Attachments open from there. The original folder is byte-for-byte unchanged and its database was never opened or migrated. No database was quarantined or replaced. |
| `after-reinstall` | Uninstalled, reinstalled, same folder chosen | Same records again. The reinstall did not start an empty shop. Nothing deleted from the data folder. |

It only reads **copies** of the database, never the live files. It **refuses to run** (exit code 2) when:
- it is run in the development account (`newth`);
- Pharma Remit is open;
- a folder is outside the current profile;
- the database key cannot be decrypted by this Windows account (DPAPI ties it to the account that made it);
- the shop is not named exactly **Migration Test Pharmacy**.

Exit codes: 0 passed, 1 a check failed, 2 refused to run. Reports go to `results\<account>\` next to the exe.

The step-by-step test (install, set up the shop, upgrade, reinstall) is the checklist written for it. A
copy for the 1.0.11 test is at `C:\Users\Public\PharmaRemitMigrationTest\CHECKLIST.txt`.

## Building it

```powershell
dotnet publish tools\store-migration-check\checker\MigrationCheck.csproj -c Release -o C:\Users\Public\PharmaRemitMigrationTest
```

This produces a single self-contained `.exe` that needs nothing installed on the test account. It
deliberately has no project references: it opens the database with the same SQLite/SQLCipher packages
as `PharmaBill.Data`.

## Proving it before use

```powershell
.\tools\store-migration-check\run-selftest.ps1
```

The script builds a separate **self-test** copy (`-p:DefineConstants=SELFTEST`) that reads a fake profile
under `%TEMP%`, never a real one. `selftest\` drives a simulated shop through upgrade and reinstall using
the real `StoreDataLocation` and `DbInitializer` code.

The clean run must pass. These five injected failures must each be caught:
- a bill lost in the move;
- a database quarantined or replaced;
- the original database changed after the move;
- the wrong shop;
- the upgrade never happening.

Never publish the self-test build: it has no account guard.
