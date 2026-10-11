using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.Data.Services;

public static class PrescriptionArchive
{
	public static string DefaultRoot => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"PharmaBill",
		"Attachments",
		"Prescriptions");

	public static Task<string> StageAsync(string sourcePath, CancellationToken cancellationToken = default)
	{
		return StoreAsync(sourcePath, DefaultRoot, "_pending", moveWhenAlreadyArchived: false, cancellationToken);
	}

	public static async Task<string?> StoreForBillAsync(string? sourcePath, bool required, string? archiveRoot, string billNo, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(sourcePath))
		{
			if (required)
			{
				throw new InvalidOperationException("Attach a prescription image or PDF for H1, X, and NDPS items.");
			}

			return null;
		}

		string root = string.IsNullOrWhiteSpace(archiveRoot) ? DefaultRoot : archiveRoot;
		return await StoreAsync(sourcePath, root, billNo, moveWhenAlreadyArchived: false, cancellationToken);
	}

	private static async Task<string> StoreAsync(string sourcePath, string root, string folderKey, bool moveWhenAlreadyArchived, CancellationToken cancellationToken)
	{
		string fullPath = Path.GetFullPath(sourcePath);
		string extension = Path.GetExtension(fullPath).ToLowerInvariant();
		if (extension is not ".pdf" and not ".jpg" and not ".jpeg" and not ".png")
		{
			throw new InvalidOperationException("Prescription attachment must be a PDF, JPG, or PNG.");
		}

		if (!File.Exists(fullPath))
		{
			throw new FileNotFoundException("The prescription attachment could not be found.", fullPath);
		}

		string directory = Path.Combine(root, DateTime.Now.Year.ToString(), Sanitize(folderKey));
		Directory.CreateDirectory(directory);
		string destination = NextAvailablePath(directory, Sanitize(Path.GetFileName(fullPath)));
		if (string.Equals(fullPath, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
		{
			return destination;
		}

		bool alreadyArchived = fullPath.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
		if (alreadyArchived && moveWhenAlreadyArchived)
		{
			File.Move(fullPath, destination);
			return destination;
		}

		await using FileStream source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
		await using FileStream target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
		await source.CopyToAsync(target, cancellationToken);
		return destination;
	}

	private static string NextAvailablePath(string directory, string fileName)
	{
		string candidate = Path.Combine(directory, fileName);
		if (!File.Exists(candidate))
		{
			return candidate;
		}

		string name = Path.GetFileNameWithoutExtension(fileName);
		string extension = Path.GetExtension(fileName);
		for (int index = 2; index < 1000; index++)
		{
			candidate = Path.Combine(directory, $"{name} ({index}){extension}");
			if (!File.Exists(candidate))
			{
				return candidate;
			}
		}

		return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}{extension}");
	}

	private static string Sanitize(string value)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		string cleaned = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
		return string.IsNullOrWhiteSpace(cleaned) ? "bill" : cleaned;
	}
}
