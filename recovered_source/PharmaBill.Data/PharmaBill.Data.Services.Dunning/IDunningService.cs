using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.Data.Services.Dunning;

public interface IDunningService
{
	Task<IReadOnlyList<CustomerCreditProfile>> GetCreditProfilesAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<CustomerCreditProfile?> GetCreditProfileAsync(Guid customerId, CancellationToken cancellationToken = default(CancellationToken));

	Task<IReadOnlyList<DunningReceivableRow>> GetOpenReceivablesAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<CreditGateAlert> EvaluateCreditGateAsync(Guid customerId, decimal additionalInvoiceAmount = 0m, CancellationToken cancellationToken = default(CancellationToken));

	IReadOnlyList<DunningWhatsAppMessage> BuildWhatsAppReminders(IReadOnlyList<DunningReceivableRow> rows, string pharmacyName, string? paymentDetails);
}
