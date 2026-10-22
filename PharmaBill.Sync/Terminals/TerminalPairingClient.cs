using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PharmaBill.Core;

namespace PharmaBill.Sync.Terminals;

/// <summary>This PC's pairing with a billing-counter server: where it is, which certificate to accept, and its key.</summary>
public sealed record TerminalClientSettings(string ServerHost, int ServerPort, string ServerFingerprint, Guid DeviceId, string Secret, string ServerName, string ShopName, string TerminalName, DateTime PairedAtUtc);

/// <summary>Kept with this PC and protected with Windows DPAPI: the key is useless copied anywhere else.</summary>
public sealed class TerminalClientSettingsStore(string directory)
{
	private readonly string _path = Path.Combine(directory, "terminal-client.dat");

	public event Action<TerminalClientSettings?>? Changed;

	public TerminalClientSettings? Load()
	{
		if (!File.Exists(_path))
		{
			return null;
		}
		try
		{
			return JsonSerializer.Deserialize<TerminalClientSettings>(ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser));
		}
		catch (Exception exception) when (exception is CryptographicException or JsonException)
		{
			return null;
		}
	}

	public void Save(TerminalClientSettings settings)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
		string temp = $"{_path}.{Guid.NewGuid():N}.tmp";
		File.WriteAllBytes(temp, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(settings), null, DataProtectionScope.CurrentUser));
		File.Move(temp, _path, overwrite: true);
		Changed?.Invoke(settings);
	}

	public void Clear()
	{
		if (File.Exists(_path))
		{
			File.Delete(_path);
		}
		Changed?.Invoke(null);
	}
}

public sealed class TerminalPairingException(string message) : Exception(message);

// The terminal side of pairing. The code is typed here and never sent: the terminal proves it knows the code for
// the certificate it actually sees, and accepts the server only when the server proves it knows the code too.
public static class TerminalPairingClient
{
	public static async Task<TerminalClientSettings> PairAsync(string host, int port, string code, string terminalName, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(host, nameof(host));
		string normalized = TerminalPairingProof.Normalize(code);
		if (normalized.Length != TerminalPairingProof.CodeLength || normalized.Any(c => !TerminalPairingProof.Alphabet.Contains(c)))
		{
			throw new TerminalPairingException($"Enter the {TerminalPairingProof.CodeLength}-character code shown on the server's screen.");
		}
		string? seen = null;
		using var handler = new HttpClientHandler
		{
			// Before pairing there is nothing to pin yet: note the certificate on each connection. The proofs below
			// are what decide whether it is the right server.
			ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
			{
				if (certificate == null)
				{
					return false;
				}
				string fingerprint = TerminalServerCertificate.Thumbprint(certificate);
				if (seen != null && seen != fingerprint)
				{
					return false;
				}
				seen = fingerprint;
				return true;
			}
		};
		using var http = new HttpClient(handler) { BaseAddress = new Uri($"https://{host.Trim()}:{port}{TerminalProtocol.BasePath}/"), Timeout = TimeSpan.FromSeconds(20) };
		var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		TerminalPairingChallenge challenge;
		try
		{
			challenge = await http.GetFromJsonAsync<TerminalPairingChallenge>("pair", json, cancellationToken) ?? throw new TerminalPairingException("The server did not answer.");
		}
		catch (HttpRequestException exception)
		{
			throw new TerminalPairingException($"Could not reach a Pharma Remit server at {host}:{port}. Check the address, that both PCs are on the shop's network, and that the server is switched on. ({exception.Message})");
		}
		if (challenge.ProtocolVersion != TerminalProtocol.Version)
		{
			throw new TerminalPairingException("The server runs a different version of Pharma Remit. Update both PCs to the same version.");
		}
		if (!challenge.Open || string.IsNullOrEmpty(challenge.ServerNonce))
		{
			throw new TerminalPairingException("No pairing code is open on the server. On the server, open Sync Station > Devices and press \"Add a billing counter\".");
		}
		if (challenge.Iterations != TerminalPairingProof.Iterations || seen == null)
		{
			throw new TerminalPairingException("The server's pairing answer was not as expected. Nothing was saved.");
		}
		string fingerprint = seen;
		string terminalNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
		string name = string.IsNullOrWhiteSpace(terminalName) ? Environment.MachineName : terminalName.Trim();
		if (name.Length > 60)
		{
			name = name[..60];
		}
		byte[] key = await Task.Run(() => TerminalPairingProof.Key(normalized, challenge.ServerNonce, terminalNonce), cancellationToken);
		try
		{
			string proof = TerminalPairingProof.TerminalProof(key, fingerprint, challenge.ServerNonce, terminalNonce, name);
			HttpResponseMessage response = await http.PostAsJsonAsync("pair", new TerminalPairRequest(name, terminalNonce, proof), json, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				TerminalError? error = await ReadErrorAsync(response, json, cancellationToken);
				throw new TerminalPairingException(error?.Error ?? $"Pairing was refused ({(int)response.StatusCode}).");
			}
			TerminalPairResponse paired = await response.Content.ReadFromJsonAsync<TerminalPairResponse>(json, cancellationToken) ?? throw new TerminalPairingException("The server's answer was empty.");
			if (seen != fingerprint || !TerminalPairingProof.Matches(TerminalPairingProof.ServerProof(key, fingerprint, challenge.ServerNonce, terminalNonce, paired.DeviceId), paired.ServerProof))
			{
				throw new TerminalPairingException("The server could not prove it is the PC showing this code. Nothing was saved. If this happens again, someone may be interfering with the shop network.");
			}
			return new TerminalClientSettings(host.Trim(), port, fingerprint, paired.DeviceId, paired.Secret, paired.ServerName, paired.ShopName, name, BusinessClock.UtcNow);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(key);
		}
	}

	private static async Task<TerminalError?> ReadErrorAsync(HttpResponseMessage response, JsonSerializerOptions json, CancellationToken cancellationToken)
	{
		try
		{
			return await response.Content.ReadFromJsonAsync<TerminalError>(json, cancellationToken);
		}
		catch (Exception exception) when (exception is JsonException or NotSupportedException)
		{
			return null;
		}
	}
}
