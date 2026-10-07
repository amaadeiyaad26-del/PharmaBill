using System;

namespace PharmaBill.App.Services;

public sealed record PendingUserManualEmail(string RecipientEmail, string PharmacyName, string OwnerName, DateTime QueuedAtUtc, bool ForceResend);
