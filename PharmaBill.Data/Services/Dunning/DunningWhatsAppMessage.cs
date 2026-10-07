using System;

namespace PharmaBill.Data.Services.Dunning;

public sealed record DunningWhatsAppMessage(Guid CustomerId, string CustomerName, string? Phone, string Body, string WaMeUrl);
