using System.Collections.Generic;
using System.Threading.Tasks;

namespace PharmaBill.Core.Ai;

public interface IAiPharmacyAssistantService
{
	Task<List<MedicineSubstituteDto>> FindSubstitutesAsync(string saltComposition, int targetQuantity);

	Task<PrescriptionParseResultDto> ParsePrescriptionImageAsync(byte[] imageBytes);

	Task<InventoryForecastReportDto> GenerateStockAdvisoryAsync();
}
