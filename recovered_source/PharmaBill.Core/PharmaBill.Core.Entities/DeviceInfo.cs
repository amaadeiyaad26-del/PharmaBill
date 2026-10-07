using System;

namespace PharmaBill.Core.Entities;

public sealed class DeviceInfo : EntityBase
{
	public string DeviceName { get; set; } = string.Empty;

	public string Platform { get; set; } = string.Empty;

	public string? AppVersion { get; set; }

	public DateTime? LastSyncAtUtc { get; set; }

	public bool IsCurrentDevice { get; set; }
}
