# Support: both versions of Pharma Remit installed on one computer

A shop can end up with the **Microsoft Store version** and the **website version** (the installer downloaded from
pharmaremit.com, or copied from USB) on the same computer. They do not share records. Each keeps its own bills, stock
and registers, and both number invoices the same way, so two sets of records that disagree build up from the day the
second one is used.

Pharma Remit now warns about this:

- The website installer stops when it finds the Microsoft Store version installed.
- Both apps show a warning on the dashboard when the other version is installed.
- The Microsoft Store version never copies the website version's records on its first start without asking.

A shop that already has both installed should end up with exactly one. The steps below get them there without losing
anything.

## 1. Find out which installation holds the real records

The real records are the ones the shop has been billing in. Check both versions; don't guess.

1. **Note the window title and the version.** Open each version (Start menu → Pharma Remit; the Microsoft Store one is
   listed as an app, the website one as a program in *C:\Program Files\Pharma Remit*). In each, open
   **Settings › About**. The Microsoft Store version shows a "Microsoft Store edition" badge; the website version shows
   none.
2. **Note where each keeps its records.**
   - Website version: always `%LocalAppData%\PharmaBill`, one sub-folder per pharmacy (for example
     `C:\Users\<user>\AppData\Local\PharmaBill\<pharmacy name>\pharmabill.db`).
   - Microsoft Store version: the folder chosen the first time it opened, by default `%UserProfile%\Pharma Remit Data`
     (for example `C:\Users\<user>\Pharma Remit Data`). The dashboard warning names both folders.
3. **Compare the latest activity in each:**
   - In each version, open **Reports › Sale Book (daily)** for the last week. Also press **Recent Bills** on the billing screen.
   - The version with today's and this week's bills is the one the shop uses.
   - If **both** have recent bills, the shop has been billing in both. Stop: do not uninstall either. Note the last
     invoice number and date in each and contact support. The two sets must be reconciled by hand. Neither app merges them.

## 2. Back up the installation you are keeping

In the version you are keeping:

1. **Settings › Data & Maintenance › Backup & Sync › Back up now**. Set a backup password if asked, and write it
   down. You cannot restore the backup without it.
2. Choose a folder **outside** the computer if you can: a USB drive, or a OneDrive/Google Drive/Dropbox folder. The
   backup file (`<pharmacy name> <date> <time>.pbbak`) is encrypted.
3. Check the file is there and its size is not tiny (a few hundred kilobytes at least for a shop with any history).

For extra safety, also copy the whole data folder from step 1.2 to the USB drive while Pharma Remit is closed.

## 3. Uninstall the other installation safely

Close both versions first.

- **Removing the website version:** Settings › Apps › Installed apps › *Pharma Remit* (publisher S.A.E.R Soft) ›
  Uninstall. This removes the program only. Its records in `%LocalAppData%\PharmaBill` are **left in place**, so nothing
  is lost if this turns out to be the wrong one.
- **Removing the Microsoft Store version:** Settings › Apps › Installed apps › *Pharma Remit* (Microsoft Store) ›
  Uninstall. Its records in the folder it chose (default `%UserProfile%\Pharma Remit Data`) are **left in place**.
  Windows does delete the Store app's own private storage, which holds only that copy's settings and licence activation
  (see below), not the records.

Both look the same in the apps list. Tell them apart by the "Microsoft Store" or "S.A.E.R Soft" publisher line, or by
the location *C:\Program Files\Pharma Remit* for the website version.

After uninstalling, open the remaining version and check the dashboard. The warning about two installations no longer
appears, and today's bills are there.

### If the Microsoft Store version took over the website version's records

When the Microsoft Store version opens for the first time and finds the website version's records, it asks what to do.
If the pharmacist chose **"Use these records in this app"**:

- The records were copied to the folder they chose and checked.
- The website version now refuses to open them. It shows "Your pharmacy records were moved to the Microsoft Store
  version" and closes.
- The original files in `%LocalAppData%\PharmaBill` are left untouched as a safety copy. Do not delete them until the
  Store version has been in use for a while and a backup has been taken.

Uninstall the website version.

## 4. Licence activation

- **An activation key is tied to the computer, not to the version.** It is checked against this computer's Machine ID
  (shown on the activation screen). The same key works in either version on the same computer.
- **The website version** keeps its activation in `%LocalAppData%\PharmaBill\data` and the Windows registry.
  Uninstalling it does not remove them.
- **The Microsoft Store version** keeps its activation in its own private storage, which Windows deletes when the Store
  version is uninstalled. If it is reinstalled, or it is the version being kept and it was never activated, enter the
  activation key again.
- **If the key is lost:** open the activation screen in the version being kept, note the Machine ID, and contact support
  for the key issued to that Machine ID. A computer whose motherboard or Windows installation changed has a new Machine ID
  and needs a new key.

## 5. Paired phones and billing counters

Phones and billing counters are paired with one installation's device identity.

- If the shop keeps the version they were already paired with, nothing changes.
- If the Microsoft Store version took over the website version's records, it has its **own** device identity. Two
  installations never share one, because that would break sync. Pair the phones and billing counters again from
  **Settings › Data & Maintenance › Backup & Sync**.

## Quick answers

- **"I see two Pharma Remit icons."** Two installations. Follow steps 1–3.
- **"Yesterday's bills are missing."** They were probably made in the other installation. Open it before doing anything
  else (step 1).
- **"The website installer says the Microsoft Store version is installed."** It stops on purpose. Keep using the
  Microsoft Store version, or uninstall it first (after a backup) if the shop wants the website version. IT staff can
  override with `/INSTALLBESIDESTORE=yes` on a silent install, or by confirming twice in the installer, only for a
  genuinely separate pharmacy on the same computer.
