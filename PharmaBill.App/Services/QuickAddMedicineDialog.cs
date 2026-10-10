using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Ai;
using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

public sealed class QuickAddMedicineDialog(IServiceScopeFactory scopes, CurrentSession session) : IQuickAddMedicineDialog
{
	public Task<CustomMedicineResult?> ShowAsync(string? brandName, SmartDrugSuggestion? suggestion = null, string? notice = null, string? barcode = null)
	{
		QuickAddMedicineWindow window = new QuickAddMedicineWindow(scopes, session.User?.Id, brandName, suggestion, notice, barcode);
		Window? owner = Application.Current?.MainWindow;
		if (owner != null && owner.IsLoaded)
		{
			window.Owner = owner;
		}

		bool? accepted = window.ShowDialog();
		return Task.FromResult(accepted == true ? window.Result : null);
	}
}
