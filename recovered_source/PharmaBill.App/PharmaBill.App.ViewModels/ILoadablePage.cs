using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.App.ViewModels;

public interface ILoadablePage
{
	Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken));
}
