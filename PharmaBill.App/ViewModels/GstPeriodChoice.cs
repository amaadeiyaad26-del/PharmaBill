using System;

namespace PharmaBill.App.ViewModels;

public sealed record GstPeriodChoice(string Key, string DisplayName, DateOnly From, DateOnly To);
