using System;

namespace PharmaBill.Data.Services;

public sealed record CustomerAgeing(Guid CustomerId, string CustomerName, string? Phone, decimal Current0To30, decimal Days31To60, decimal Days61To90, decimal Over90, decimal Total);
