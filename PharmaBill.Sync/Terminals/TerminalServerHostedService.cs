using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PharmaBill.Sync.Terminals;

public sealed record TerminalServerSettings(bool Enabled, int Port = TerminalProtocol.DefaultPort);

// Whether this PC serves billing terminals. Off unless the owner turns it on (the Devices panel, step 2), so no
// shop starts listening for terminals by accident.
public sealed class TerminalServerSettingsStore(string directory)
{
	private readonly string _path = Path.Combine(directory, "terminal-server.json");

	public TerminalServerSettings Load()
	{
		try
		{
			return File.Exists(_path) ? JsonSerializer.Deserialize<TerminalServerSettings>(File.ReadAllText(_path)) ?? new TerminalServerSettings(false) : new TerminalServerSettings(false);
		}
		catch (Exception exception) when (exception is IOException or JsonException)
		{
			return new TerminalServerSettings(false);
		}
	}

	public void Save(TerminalServerSettings settings)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
		string temp = $"{_path}.{Guid.NewGuid():N}.tmp";
		File.WriteAllText(temp, JsonSerializer.Serialize(settings));
		File.Move(temp, _path, overwrite: true);
	}
}

public sealed class TerminalServerHostedService(TerminalServer server, TerminalServerSettingsStore settings, ILogger<TerminalServerHostedService> logger) : IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken)
	{
		TerminalServerSettings current = settings.Load();
		if (!current.Enabled)
		{
			return Task.CompletedTask;
		}
		_ = Task.Run(async () =>
		{
			try
			{
				await server.StartAsync(current.Port, cancellationToken: CancellationToken.None);
			}
			catch (Exception exception)
			{
				logger.LogWarning(exception, "Terminal server could not start on port {Port}.", current.Port);
			}
		}, CancellationToken.None);
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken) => server.StopAsync();
}
