using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Windows;

namespace PharmaBill.App.Services;

public static class BrandAssets
{
	public const string SupportEmail = "pharma.bill26@gmail.com";

	public const string MailtoUri = "mailto:pharma.bill26@gmail.com";

	public const string SupportMailtoWithSubject = "mailto:pharma.bill26@gmail.com?subject=PharmaBill%20Support%20Request";

	public const string DeveloperAttribution = "Designed and developed by S.A.E.R. to serve pharma professionals. For any query: email at pharma.bill26@gmail.com";

	public const string ErrorDialogSupportHint = "Encountering an issue? Contact developer support at pharma.bill26@gmail.com with your log folder details.";

	public const string AndroidPlayStoreUrl = "https://play.google.com/store/search?q=PharmaBill%20SAER&c=apps";

	public const string ReleaseManifestUrl = "https://raw.githubusercontent.com/SAER-PharmaBill/PharmaBill/main/docs/release-manifest.json";

	public const string ReleaseDownloadUrl = "https://github.com/SAER-PharmaBill/PharmaBill/releases/latest";

	public const string MicrosoftStoreProductId = "";

	public const string BrandIconPackUri = "pack://application:,,,/PharmaBill.App;component/Assets/Icons/pharma_bill_3d.png";

	public const string PdfWatermarkPackUri = "pack://application:,,,/PharmaBill.App;component/Assets/Icons/pharma_bill_3d_watermark.png";

	private static readonly object Gate = new object();

	private static byte[]? _watermarkPng;

	public static byte[]? TryGetWatermarkPng()
	{
		byte[] array = _watermarkPng;
		if (array != null && array.Length > 0)
		{
			return _watermarkPng;
		}
		lock (Gate)
		{
			array = _watermarkPng;
			if (array != null && array.Length > 0)
			{
				array = _watermarkPng;
			}
			else
			{
				_watermarkPng = LoadWatermarkPng();
				byte[] watermarkPng = _watermarkPng;
				array = ((watermarkPng != null && watermarkPng.Length > 0) ? _watermarkPng : null);
			}
		}
		return array;
	}

	private static byte[]? LoadWatermarkPng()
	{
		string[] array = new string[2] { "assets/icons/pharma_bill_3d_watermark.png", "assets/icons/pharma_bill_3d.png" };
		foreach (string text in array)
		{
			byte[] array2 = TryLoadPack("pack://application:,,,/PharmaBill.App;component/" + text);
			if (array2 != null && array2.Length > 0)
			{
				return array2;
			}
			byte[] array3 = TryLoadGResources(text);
			if (array3 != null && array3.Length > 0)
			{
				return array3;
			}
		}
		foreach (string item in EnumerateFileCandidates())
		{
			if (File.Exists(item))
			{
				return File.ReadAllBytes(item);
			}
		}
		return null;
	}

	private static byte[]? TryLoadPack(string packUri)
	{
		try
		{
			_ = PackUriHelper.UriSchemePack;
			Stream stream = Application.GetResourceStream(new Uri(packUri, UriKind.Absolute))?.Stream;
			if (stream != null)
			{
				return ReadAll(stream);
			}
		}
		catch
		{
		}
		return null;
	}

	private static byte[]? TryLoadGResources(string resourceKey)
	{
		try
		{
			using Stream stream = typeof(BrandAssets).Assembly.GetManifestResourceStream("PharmaBill.App.g.resources");
			if (stream == null)
			{
				return null;
			}
			using ResourceReader resourceReader = new ResourceReader(stream);
			foreach (DictionaryEntry item in resourceReader)
			{
				if (item.Key is string text && text.Equals(resourceKey, StringComparison.OrdinalIgnoreCase))
				{
					object value = item.Value;
					byte[] result;
					if (value is Stream stream2)
					{
						result = ReadAll(stream2);
					}
					else
					{
						result = ((!(value is byte[] array)) ? null : array);
					}
					return result;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static IEnumerable<string> EnumerateFileCandidates()
	{
		string baseDir = AppContext.BaseDirectory;
		yield return Path.Combine(baseDir, "Assets", "Icons", "pharma_bill_3d_watermark.png");
		yield return Path.Combine(baseDir, "Assets", "Icons", "pharma_bill_3d.png");
		InlineArray5<string> buffer = default;
		buffer[0] = baseDir;
		buffer[1] = "PharmaBill.App";
		buffer[2] = "Assets";
		buffer[3] = "Icons";
		buffer[4] = "pharma_bill_3d_watermark.png";
		yield return Path.Combine(buffer);
		string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		if (!string.IsNullOrWhiteSpace(asmDir))
		{
			yield return Path.Combine(asmDir, "Assets", "Icons", "pharma_bill_3d_watermark.png");
			yield return Path.Combine(asmDir, "Assets", "Icons", "pharma_bill_3d.png");
		}
	}

	private static byte[] ReadAll(Stream stream)
	{
		using MemoryStream memoryStream = new MemoryStream();
		stream.CopyTo(memoryStream);
		return memoryStream.ToArray();
	}
}
