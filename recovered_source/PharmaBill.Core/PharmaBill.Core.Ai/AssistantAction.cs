using System;

namespace PharmaBill.Core.Ai;

public sealed record AssistantAction(bool Recognised, string Message, string? SectionKey = null, string? ReportKey = null, DateOnly? From = null, DateOnly? To = null);
