using System;

namespace PharmaBill.Data.Services;

public sealed record UsbBackupSettings(DateTime? LastUsbBackupUtc, DateTime? ReminderSnoozeUntilUtc, string? LastUsbDriveRoot);
