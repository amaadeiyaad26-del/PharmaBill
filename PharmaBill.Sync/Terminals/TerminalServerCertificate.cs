using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PharmaBill.Sync.Terminals;

// The server PC's TLS identity for terminals. Self-signed and made once per PC; a terminal pins its SHA-256
// fingerprint when it pairs, so no certificate authority is involved and a look-alike server is refused.
// Stored DPAPI-protected next to this PC's other local state.
public sealed class TerminalServerCertificate
{
	private readonly string _path;

	private readonly object _gate = new object();

	private X509Certificate2? _certificate;

	public TerminalServerCertificate(string directory)
	{
		_path = Path.Combine(directory, "terminal-server-certificate.dat");
	}

	public X509Certificate2 Get()
	{
		lock (_gate)
		{
			if (_certificate != null)
			{
				return _certificate;
			}
			if (File.Exists(_path))
			{
				byte[] pfx = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
				return _certificate = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
			}
			byte[] created = Create();
			Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
			string temp = $"{_path}.{Guid.NewGuid():N}.tmp";
			File.WriteAllBytes(temp, ProtectedData.Protect(created, null, DataProtectionScope.CurrentUser));
			File.Move(temp, _path, overwrite: true);
			return _certificate = X509CertificateLoader.LoadPkcs12(created, null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
		}
	}

	/// <summary>What a terminal pins: SHA-256 of the certificate, upper-case hex.</summary>
	public string Fingerprint => Thumbprint(Get());

	public static string Thumbprint(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.RawData));

	private static byte[] Create()
	{
		using RSA key = RSA.Create(2048);
		var request = new CertificateRequest($"CN=Pharma Remit server ({Environment.MachineName})", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
		request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
		var names = new SubjectAlternativeNameBuilder();
		names.AddDnsName(Environment.MachineName);
		names.AddIpAddress(IPAddress.Loopback);
		request.CertificateExtensions.Add(names.Build());
		using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
		return certificate.Export(X509ContentType.Pkcs12);
	}
}
