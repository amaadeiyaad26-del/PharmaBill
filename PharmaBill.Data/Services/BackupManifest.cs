using System;
using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record BackupManifest(string Format, int BackupVersion, string ApplicationVersion, IReadOnlyList<string> AppliedMigrations, DateTime CreatedAtUtc, long DatabaseBytes, IReadOnlyDictionary<string, long> TableRowCounts, IReadOnlyDictionary<string, string> KeyTableChecksums, IReadOnlyList<string> Attachments);
