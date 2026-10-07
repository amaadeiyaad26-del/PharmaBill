using System;
using System.Net.Http;

namespace PharmaBill.Sync;

public sealed class DefaultCloudHttpClientProvider : ICloudHttpClientProvider
{
	private readonly object _gate = new object();

	private HttpClient? _client;

	private string _signature = string.Empty;

	public HttpClient Create(CloudSyncSettings settings)
	{
		lock (_gate)
		{
			string text = settings.ServerUrl + "|" + settings.ApiKey;
			if (_client == null || text != _signature)
			{
				_client?.Dispose();
				_client = new HttpClient
				{
					BaseAddress = new Uri(settings.ServerUrl),
					Timeout = TimeSpan.FromSeconds(60L)
				};
				_signature = text;
			}
			return _client;
		}
	}
}
