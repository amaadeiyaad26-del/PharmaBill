using System;

namespace PharmaBill.Core.Entities;

public sealed class AppUser : EntityBase
{
	public string DisplayName { get; set; } = string.Empty;

	public string UserName { get; set; } = string.Empty;

	public string? Phone { get; set; }

	public string? Email { get; set; }

	public UserRole Role { get; set; }

	public string PasswordHash { get; set; } = string.Empty;

	public int IdleLockMinutes { get; set; } = 10;

	public bool UseWindowsHello { get; set; }

	public bool IsActive { get; set; } = true;

	public DateTime? LastLoginAtUtc { get; set; }

	public string? AuthProvider { get; set; }

	public string? ProviderSubjectId { get; set; }
}
