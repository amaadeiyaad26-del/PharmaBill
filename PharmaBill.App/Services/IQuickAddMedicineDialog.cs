using System.Threading.Tasks;
using PharmaBill.Core.Ai;
using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

public interface IQuickAddMedicineDialog
{
	Task<CustomMedicineResult?> ShowAsync(string? brandName, SmartDrugSuggestion? suggestion = null, string? notice = null, string? barcode = null);
}
