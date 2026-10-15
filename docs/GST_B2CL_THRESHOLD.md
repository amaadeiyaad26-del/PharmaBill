# B2CL (B2C Large) invoice threshold

## What it is

GSTR-1 Table 5 ("B2C Large") lists, invoice by invoice with place of supply, **inter-state** supplies to
**unregistered** buyers whose invoice value is above a threshold. Smaller ones are summarised in Table 7 (B2CS).

## Current default and its source

| | |
|---|---|
| Default in PharmaBill | **₹1,00,000** |
| Source | CBIC **Notification No. 12/2024–Central Tax**, dated **10 July 2024**, effective **1 August 2024** |
| Previous value | ₹2,50,000 |

**Verification status:** this notification number, its date and the amount were entered from memory when the
setting was introduced (15 Oct 2026). They have **not** been checked against the official notification text.
Confirm the current threshold with a CA, or against the notification on the CBIC/GST portal, before relying on
it for a filing. If you confirm it, record who confirmed it and when below.

| Confirmed by | Date | Notes |
|---|---|---|
| — | — | Not yet confirmed |

## Where it lives

- **Default constant:** `PharmaBill.Core/Gst/GstThresholds.cs` → `DefaultB2cLargeInvoiceThreshold`.
- **Per-pharmacy setting:** `PharmacyProfile.B2cLargeInvoiceThreshold`, editable in
  **Settings → Firm Profile → "B2C-large (B2CL) invoice threshold"**. The field label shows the source and asks the
  user to confirm with a CA.
- **Existing databases:** migration `UnregisteredBuyerPlaceOfSupply` set the column to ₹1,00,000 for every
  pharmacy profile that already existed.
- **One-time reminder:** the GST & e-Way screen shows "B2CL threshold is set to ₹…. Confirm this with your
  accountant before filing." once per pharmacy store, until dismissed. Dismissals are kept in
  `%LocalAppData%\PharmaBill\data\dismissed-notices.json` under the key `b2cl-threshold-confirm:<store>`.

## What changing it affects

1. **GSTR-1 offline JSON export** (`GstReturnExportService.BuildB2cl`): which inter-state unregistered invoices
   go to `b2cl` (invoice-wise) instead of `b2cs` (summary). Changing the value changes what is filed for any
   period exported after the change.
2. **Wholesale invoice save** (`WholesaleInvoiceService`): for an unregistered buyer with no state on record, the
   place of supply normally defaults to the pharmacy's own state. Above this threshold the invoice is refused
   until the buyer's state (or a valid GSTIN) is entered, because place of supply must be reported.

## If the law changes

1. Update `DefaultB2cLargeInvoiceThreshold` and this document (new notification number, date, effective date).
2. Existing pharmacies keep their saved value. Decide whether to write a migration that updates profiles still on
   the old default, and say so in the release notes.
3. Remember that a different threshold applies by **invoice date**: filings for periods before the effective date
   used the old value.
4. To show the reminder again after a change, use a new notice key (e.g. `b2cl-threshold-confirm-2:<store>`).
