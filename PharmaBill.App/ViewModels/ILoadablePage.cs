
namespace PharmaBill.App.ViewModels;

public interface ILoadablePage
{
	Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken));
}
