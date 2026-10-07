using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.Core.Ai;

public interface IOcrTextRecognizer
{
	Task<string> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken = default(CancellationToken));
}
