using System;

namespace PharmaBill.Data.Services;

public sealed record BranchOption(Guid Id, string Code, string Name, bool IsHeadOffice, bool IsCurrent);
