using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record GstTaxAuditResult(bool IsClean, string StatusLabel, IReadOnlyList<GstTaxAuditIssue> Issues);
