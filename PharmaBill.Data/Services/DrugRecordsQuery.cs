using System;
using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record DrugRecordsQuery(IReadOnlyCollection<Guid> DrugIds, DateTime StartUtc, DateTime EndUtc, string RecordType = "Both");
