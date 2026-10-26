
namespace PharmaBill.Sync;

public interface ICloudHttpClientProvider
{
	HttpClient Create(CloudSyncSettings settings);
}
