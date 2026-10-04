# PharmaBill Sync Protocol

**Status: Approved for initial offline implementation.** This document defines
the offline sync contract for PharmaBill Windows and Android. No network transport is part of this protocol.
Implementations must not sync until both apps use the same canonical payloads,
clock rules and conflict policies described here.

## 1. Scope and compatibility

- Sync is device-to-device and store-and-forward: create an export package,
  transfer it by USB or user-selected file sharing, then import it offline.
- The database remains authoritative for each device's local working copy.
  Imported packages are validated and applied atomically; importing the same
  package more than once is safe.
- Use the entity/table names and business rules from `docs/DATA_CONTRACT.md`
  and `docs/android-schema/`. Do not rename Android fields to match C# names.
- Package `protocolVersion` and each change's `schemaVersion` are required.
  Reject unsupported protocol versions. A change with an unsupported schema
  version is not partially applied.
- This document defines canonical logical payloads. Database-only metadata,
  SQLite row layout and EF property names are not the wire contract.

## 2. Device identity and pairing

- Each installation has a stable, random `deviceId` (UUID) and a display name.
  The registry entry records platform, app version and last successful import
  or export time.
- Pairing is user-confirmed and offline. One device generates a cryptographically
  random, single-use pairing code with an expiry; both users compare/enter the
  code out of band. A successful pairing establishes a unique random 256-bit
  device key for that peer. The code is not reused as the long-term key.
- Store device keys encrypted at rest using the platform's protected storage.
  Never export a device key or include it in a sync package. Revoking a peer
  stops accepting packages authenticated by that key.
- Packages are authenticated with HMAC-SHA-256 over the exact package
  manifest bytes and every payload/file entry's path, length and SHA-256 hash,
  using the key of the exporting paired device. Compare MACs in constant time.
  An unknown or revoked device, invalid MAC, duplicate entry path or hash
  mismatch rejects the entire package.

## 3. Hybrid logical clock

Every syncable entity has an `hlcStamp` string in this canonical form:

```text
<unix-milliseconds>:<logical-counter>:<device-uuid>
```

- The first two components are non-negative base-10 integers without leading
  zeroes (except the value `0`). The device UUID is lowercase, hyphenated
  canonical UUID text.
- Compare stamps lexicographically by numeric physical milliseconds, numeric
  logical counter, then lowercase device UUID using ordinal byte ordering.
  Implementations must not compare the encoded strings as ordinary text.
- `Now()` advances physical time when the local UTC clock advances; otherwise
  increment the logical counter. `Receive(remote)` first parses the remote
  stamp, then advances the local HLC to a value greater than both the local
  clock and the remote stamp, using the standard HLC max-physical-time and
  logical-counter rules. The local device ID is written into a newly generated
  stamp.
- Invalid stamps are rejected. A future remote physical time may be recorded
  as received metadata, but does not change local wall-clock time; implementations
  should flag implausible clock skew for review.
- `createdAtUtc`, `updatedAtUtc` and business timestamps are UTC ISO-8601
  strings with an explicit `Z` suffix (for example
  `2026-10-03T16:31:00.0000000Z`). Never serialize local or unspecified times.
- D2 currently emits `<ticks-hex>-<device-guid>` stamps. Before sync is enabled,
  the Windows writer must be changed to this canonical format and existing
  records must be handled as legacy data; do not parse old stamps as canonical
  HLC values.

## 4. Change envelope and payloads

Each change is identified by its immutable `ChangeLog.Id` UUID. The canonical
envelope is UTF-8 JSON:

```json
{
  "changeId": "uuid",
  "entity": "table-name",
  "entityId": "uuid",
  "operation": "Upsert",
  "schemaVersion": 1,
  "hlcStamp": "unix-ms:counter:device-uuid",
  "originDeviceId": "uuid",
  "changedAtUtc": "2026-10-03T16:31:00.0000000Z",
  "payload": {}
}
```

- `operation` is `Upsert` or `SoftDeleted`. A soft delete is a tombstone and
  never physically removes business data.
- Payload field names, nullability and enum strings follow the Android schema
  and `DATA_CONTRACT.md`; enums serialize by their documented string names,
  never as integers or localized labels.
- Money values serialize as signed integer paise, with no floating-point money
  fields. Convert decimal rupees to paise using two-decimal
  `MidpointRounding.AwayFromZero`; reject values that cannot be represented
  exactly after that contract rounding. `null` remains JSON `null`.
- JSON is UTF-8, no BOM, no duplicate object keys, and deterministic for
  signing: object properties are emitted in ordinal name order, arrays retain
  contract order, and numbers use invariant JSON number syntax.
- Payloads contain the full canonical entity state for an upsert, not a
  property delta. Device-local secrets, encryption keys, PIN/password hashes,
  subscription credentials and local absolute file paths are excluded.
- Import deduplicates by `changeId`, records successfully applied IDs, and
  never treats a repeated change ID as a new edit. A stable `changeId` must
  have identical signed contents; reuse with different contents rejects the
  package.

## 5. Apply and conflict rules

Changes are applied in ascending HLC order, then `changeId` ordinal order.
The importer still applies the policy below when a package arrives out of
order; export ordering is not a correctness guarantee.

### Master data

Pharmacy profile, licence/configuration data, users/roles, drugs/catalogue
overrides, suppliers, customers and other mutable master data use last-writer-
wins by HLC. Compare the incoming stamp with the stored entity stamp:

- A newer incoming stamp replaces the stored canonical state.
- An older incoming stamp is recorded as processed but does not replace local
  state.
- If both local and incoming branches changed since their common state and
  their canonical payloads differ, retain the winning state and write a
  `SyncConflict` containing both versions, stamps, device IDs and a review
  status. A user-reviewed resolution is a new local change with a new ID/HLC;
  never rewrite prior change history.

Implementations need enough known version/ancestry metadata to distinguish an
ordinary stale delivery from two independently changed branches. If a common
base cannot be established, differing concurrent master versions are
conservatively conflicts; do not silently discard either version.

### Immutable transaction documents

Sales, sale items, wholesale invoices and items, purchases and items, receipts,
returns/credit notes, statutory register entries, audit log entries and other
append-only documents are immutable. If the document UUID already exists,
do not overwrite it. Identical content is an idempotent duplicate. Different
content with the same UUID creates a `SyncConflict` and leaves the existing
document unchanged. Corrections are new documents, never edits or deletes.

### Stock movements and negative stock

- Apply every unique `StockMovement` by its UUID before recomputing affected
  batch quantities as the sum of all accepted movement quantities for that
  batch. `Batch.Quantity` is a cache only; it is never directly merged.
- A negative recomputed balance is accepted for replication, and creates or
  updates a visible `StockConflict` alert with batch, balance, movement IDs,
  contributing devices and resolution status. Sync must not reject or silently
  clamp the movement or balance.
- Resolving a stock conflict uses a normal audited stock adjustment movement
  with a reason. It must not edit or remove the movements that caused it.
- A late movement triggers recomputation and conflict refresh even when its
  timestamp predates movements already present.

### Atomicity and retries

Validate package authentication, structure, versions, payloads and files before
applying. Apply a package and persist processed change IDs, entity changes,
conflicts, stock recomputations and import state in one database transaction.
An interruption rolls back the transaction; retrying the package then produces
the same result. If package-level atomicity cannot be provided, no success
state may be reported and the resume cursor must identify the last committed
change unambiguously.

## 6. Attachments and file transfer queue

- Prescription images/PDFs, licence copies and bill images are referenced by
  SHA-256 content hash, byte length, media type and logical attachment ID.
  Local paths are resolved independently on each device and are never synced.
- Purchase and wholesale invoice scans use a `DocumentPath=` marker in the
  invoice notes field; that marker is stripped from the change payload and
  restored to a local path only after the attachment hash is verified.
- Hash the exact file bytes with SHA-256. A received file is not made available
  to business records until its byte length and hash have been checked.
- A `FileTransfer` queue tracks attachment ID, content hash, size, category,
  source device, queue state, retry count and last error. States are
  `Pending`, `InProgress`, `Complete` and `Failed`; retrying a transfer is
  idempotent by attachment ID and hash.
- A package may contain complete attachment entries or metadata indicating
  that a file must be transferred in a later package. Missing attachments are
  explicit and must not be represented as successful transfers.

## 7. Offline sync package

A `.pbsync` file is an uncompressed or normally compressed ZIP containing:

```text
manifest.json
changes/<change-id>.json
files/<sha256>
signature.hmac
```

The manifest includes protocol version, package UUID, creation time in UTC,
exporting device ID/app version, intended paired device ID when restricted,
change count, ordered change IDs and hashes, file hashes/sizes, and a manifest
hash. `signature.hmac` authenticates the canonical manifest and every entry
path, byte length and SHA-256 hash with the paired device key.

New packages are encrypted after ZIP creation. The outer file is a binary
envelope consisting of ASCII `PBENC1`, the lowercase exporting-device UUID in
`D` format, a 12-byte random nonce, a 16-byte AES-256-GCM tag, then ciphertext.
The UUID bytes through the fixed header are authenticated as associated data.
The AES key is `HMAC-SHA256(pair-key, UTF8("PharmaBill-sync-package-encryption-v1:" + exporting-device-uuid))`.
The paired key is protected at rest with Windows DPAPI CurrentUser scope; the
derived AES key and pairing secret are never stored.

For compatibility with existing Android builds, import also accepts the
previous ZIP-based package format only when its paired-device HMAC verifies.
Windows folder sync always publishes the encrypted envelope format.

Import verifies the encrypted envelope (when present), ZIP paths against
traversal/absolute paths, configured size limits, hashes and signature, paired
device and versions, then applies changes under the transaction rules above.
ZIP entries are staged before database mutation. No executable content is run
from a package. Export publishes through a temporary file and only exposes the
finished package after the entire encrypted envelope is written.
Folder exchange snapshots pending change IDs for every paired Android device
before marking that snapshot `Synced` locally. If any target package fails to
write, those changes remain pending; already published copies are safe to
retry because import is idempotent.

## 8. UI and observability

The Sync settings screen shows local device identity, paired/revoked devices,
last package exchange, pending changes, file-transfer queue, conflicts and
stock-conflict alerts. Pairing codes and keys are never displayed after setup.
Conflict review is read-only for original versions and requires a deliberate,
audited resolution. Export and import select package files; neither operation
requires network access.

Google Drive and other local/cloud-synced folders use `from_windows/` for
Windows-originated packages, `from_android/` for incoming Android packages,
`archive/` for successfully applied packages, and `errors/` for stable
corrupted or incomplete packages. The Windows poller scans every 60 seconds
and requires two matching size/last-write observations before importing; a
locked file is deferred rather than quarantined. Outgoing Android packages
are produced immediately by Sync Now and at least every ten minutes while
background auto-sync is enabled. Folder path, auto-sync preference and last
run statistics are DPAPI-protected local settings.

## 9. Shared conformance vectors

`docs/test-vectors/*.json` is the shared, language-neutral source of expected
results for GST-inclusive money/paise conversion, half-up rounding, HLC and
number sequencing, stock movement sums/negative-stock alerts, and conflict
ordering/resolution. xUnit and Android tests consume the same JSON bytes;
neither project maintains a forked copy with independent expected results.
Copying vectors into an Android project is permitted only as a byte-identical
build/test input. Do not invent or change legal values in vectors.

## 10. Security and error handling

Never log pairing codes, device keys, backup/database keys, patient payloads or
attachment contents. Reject malformed, unauthenticated, unsupported or
tampered packages with a clear user-visible error and an audit event that does
not disclose protected payload data. Preserve the original package for
diagnostics only when the user explicitly chooses to keep it.

## 11. Optional cloud sync (PharmaBill.CloudSync)

Cloud sync is an optional transport. It uses the same HLC (section 3), the same change envelope (section 4) and the same
apply rules (section 5) as package and folder sync, so a device can use any combination of the three.

### Tenancy
- **Tenant**: one pharmacy business. **Branch**: a location in a tenant. **Device**: belongs to exactly one branch;
  a device id that was first seen on one branch is rejected (403) from any other branch.
- Tables: `Tenants`, `Branches`, `Devices`, `ChangeLog`, `FileBlobs`. PostgreSQL in production, SQLite in development.
- The server is a single writer: pushes are serialised and each stored change gets a server HLC (`ServerHlc`) from the
  same clock algorithm (device id `00000000-0000-4000-8000-00000000c10d`). Run one server instance.

### Authentication
- Each branch has an API key `pbk_<branchId:N>_<secret>` sent as `Authorization: Bearer <key>`. The server stores only
  the SHA-256 of the secret and compares in constant time. Keys are issued once by the admin endpoints
  (`POST /api/admin/tenants`, `POST /api/admin/branches`, header `X-Admin-Key`; disabled when no admin key is configured).
- The Windows client keeps URL and key in DPAPI-protected storage. Only `https://` (or loopback `http://`) URLs are accepted.
- Missing, malformed, unknown or wrong keys and inactive branches/tenants return 401.
- Devices also send `X-Device-Id`; the server never returns a device's own changes to it.

### Endpoints
| Endpoint | Behaviour |
|---|---|
| `POST /api/sync/push` | Body `{deviceId, changes[]}` (max 500). Each change is the section 4 envelope plus optional `sharedWithBranchIds`. Idempotent by `changeId` per tenant. Returns `{accepted, duplicates, serverHlc, acknowledgedChangeIds}`. |
| `GET /api/sync/pull?since=<serverHlc>&branchId=<id>&limit=<1-500>` | `branchId` must equal the token's branch (else 403). Returns changes in server-HLC order after `since`, plus `nextSince` and `hasMore`. Clients persist `nextSince` only after applying the page. |
| `POST /api/sync/files?shared=<bool>` | Raw body, header `X-Content-Sha256`. Server recomputes the hash (400 on mismatch). Stored under `tenant/branch/hash`. |
| `GET /api/sync/files/{hash}` | Returns the blob if uploaded by the caller's branch or marked shared; otherwise 404. |

### Branch scoping
- **Global (master data, all branches of the tenant)**: Drug, Supplier, Customer, CustomerLicence, ScheduleOverride,
  LicenceRecord. Resolved on clients by the section 5 last-writer-wins rules.
- **Branch scoped (default)**: everything else, including Sale, SaleItem, Batch, StockMovement, WholesaleInvoice, ledgers,
  receipts, register entries. Visible only to devices of the originating branch.
- **Shared**: a branch-scoped change pushed with `sharedWithBranchIds` is also delivered to exactly those branches
  (all must belong to the tenant). This is the hook for inter-branch stock transfer documents.
- TODO: the Windows app does not yet create transfer documents, so it never sets `sharedWithBranchIds`.

### Windows worker
- `CloudSyncWorker` runs every 5 minutes when enabled and configured; "Sync Now" runs the same cycle.
- Push cursor: `ChangeLog.ChangedAtUtc` (inclusive, safe to resend because the server is idempotent). It is independent of
  `SyncState`, so folder/package sync and cloud sync do not hide changes from each other.
- Pull cursor: last `nextSince`. Pulled changes go through `ChangeApplier`, so applied changes never re-enter ChangeLog.
- Attachment blobs: the endpoints and `CloudSyncClient.UploadFileAsync/DownloadFileAsync` exist; the worker does not yet
  transfer licence/prescription/bill images automatically (TODO).
## Implementation notes

- The approved HLC format is `unix-milliseconds:logical-counter:device-uuid`;
  D2's tick-hex stamps are legacy and are not parsed as HLC.
- Offline package authentication uses a distinct paired-device key and
  HMAC-SHA-256. The approved package extension is `.pbsync`.
- Android project location must be identified before vectors can be copied.
  `docs/test-vectors/` remains the canonical byte-identical source.

