using System.Threading.Tasks;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App.Services;

public interface IAddStockDialogService
{
	Task<bool> ShowAsync(AddStockRequest request);
}
