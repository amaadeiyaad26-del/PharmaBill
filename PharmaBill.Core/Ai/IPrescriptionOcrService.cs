using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.Core.Ai;

public interface IPrescriptionOcrService
{
	bool IsCloudConfigured { get; }

	Task<PrescriptionParseResultDto> ParseAsync(byte[] imageBytes, string mimeType, bool allowCloud, CancellationToken cancellationToken = default(CancellationToken));
}
