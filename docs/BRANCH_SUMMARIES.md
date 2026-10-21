# Branch summaries over exported files

**Status: agreed design, not built.** This is how branch summaries will be rebuilt if
multi-branch reporting comes back. Nothing in this document is implemented yet.

## Why this exists

Branches used to share summaries through a linked Google account. Each branch uploaded
files to `PharmaBill_Sync/Branches/<code>/` on Google Drive, and the head office
downloaded them. That route was removed along with Google Drive backup (see
`FolderBackupService`). It probably never ran at a shop: the built-in Google client
credentials were always placeholders, and every branch had to sign in to the same
Google account.

The old route fed two features. The billing hint stays in the code; the report rows are
removed for now:

- **Billing search.** When a medicine is out of stock here, search shows "Branch X had
  N in stock at <time>" (`BranchService.FindPeerAvailability`). The hint is shown only
  while the figure is under 24 hours old (`BranchService.PeerStockFreshFor`). Older
  figures are hidden, so a pharmacist never promises stock that has since been sold.
- **Reports.** The "Consolidated network summary" filter used to add one row per other
  branch to the sales and purchase reports. Both are removed:
  - The sales rows covered whatever period a branch last published (the last month by
    default), not the report's own dates.
  - The purchase rows carried sales totals, because branches never sent purchase
    figures.

  The filter still exists and shows this PC's own rows only.

The billing hint reads the head office's cache folder, `<data folder>\branch-cache\`.
Nothing fills that cache any more.

## Agreed design (Option A)

**Branch side: "Export branch summary".**

- Writes one file per branch into a folder the branch chooses once, or onto a USB stick.
- The file holds the two documents `BranchService` already builds:
  - `BuildLocalStockIndexAsync`: each medicine with stock above zero (name,
    composition key, quantity).
  - `BuildLocalSalesSummaryAsync`: bill count and sales total for a stated period.
- Name it after the branch code so that a newer export replaces the older one, for
  example `BR02 branch-summary.json`.
- Set `GeneratedAtUtc` at export time. Every consumer judges freshness from it.

**Head-office side: "Import branch summaries".**

- Reads every branch-summary file in a chosen folder.
- Saves each through `SavePeerStockIndex` and `SavePeerSalesSummary` (existing code).
- Rejects:
  - a file carrying this PC's own branch code;
  - a file that won't parse;
  - a file older than the copy already in the cache.
- Shows what it imported: branch, "as of" time, and anything it rejected and why.

**Transport.** Nothing is built for transport. If the branches share a OneDrive, Google
Drive or Dropbox folder, that sync app moves the files and the flow is close to
automatic. Without one, USB or e-mail works. It is the same approach as folder backup.

**Network rows in reports.** These come back with the rebuild, under one rule: a branch
row covers exactly the report's date range.

- **Sales report:** the export must carry figures for the requested dates. Either:
  - each branch exports per-day totals and the head office sums the report's range; or
  - the head office asks for an export of a stated range.

  A summary covering any other period is not shown.
- **Purchase report:** gets network rows only if branches export purchase figures. Sales
  totals never stand in for them.
- **Row label:** each row shows the branch's "as of" time.

**Freshness.**

- Billing search keeps the 24-hour rule above.
- If imports become routine, consider an import on a timer from the chosen folder,
  like folder backup's daily option.

## Deliberately left out

- **The full-database snapshot.** The old publish also uploaded `snapshot.db.enc`, a
  complete encrypted backup of the branch database. Nothing ever read it, and it put a
  whole branch's patient and prescription records off the PC. Do not bring it back.
  Backup is the job of `FolderBackupService`.
- **Network transport between PCs.** Branches are separate shops on separate networks,
  so a pull over the local network (for example through `LocalSyncServer` on port 5055)
  only works between sites with a VPN. It would also need PC-to-PC pairing, branch
  addresses and firewall setup at every branch. Several PCs inside one shop are already
  covered by the existing sync.

## Open decisions for whoever builds it

- **Encrypt the export or not.** The documents hold stock quantities and sales totals,
  but no patient data. Encrypting them would need a shared network password, which is
  one more thing a shop can lose.
- **How to match the report's period.** Choose between per-day totals in every export
  and an export for a requested range (see "Network rows in reports"). Per-day totals
  let one export serve any report.
- **Clearing the cache.** Decide whether head office can remove a branch that has
  closed. Today a stale cache file stays until it is deleted by hand.

## Code that already exists

| Piece | Where |
| --- | --- |
| Build stock index / sales summary | `BranchService.BuildLocalStockIndexAsync`, `BuildLocalSalesSummaryAsync` |
| Cache read/write | `BranchService.SavePeer*`, `LoadPeer*`, `BranchCacheDirectory` |
| Freshness rule for billing hints | `BranchService.IsFresh`, `PeerStockFreshFor` |
| Head-office flag | Settings → Branch → "This desktop is the head office" |
| Old Google Drive route (dormant, no UI) | `PharmaBill.Sync/BranchNetworkSyncService.cs` |
