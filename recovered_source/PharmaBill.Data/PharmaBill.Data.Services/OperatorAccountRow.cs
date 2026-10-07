using System;
using PharmaBill.Core.Entities;

namespace PharmaBill.Data.Services;

public sealed record OperatorAccountRow(Guid Id, string UserName, string DisplayName, string RoleLabel, UserRole Role, bool IsActive, DateTime CreatedAtUtc, DateTime? LastLoginAtUtc);
