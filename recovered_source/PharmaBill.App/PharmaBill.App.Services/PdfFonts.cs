using System;
using System.IO;
using System.Linq;
using System.Reflection;
using QuestPDF.Drawing;

namespace PharmaBill.App.Services;

public static class PdfFonts
{
	private static readonly object Gate = new object();

	private static bool _registered;

	public const string FamilyName = "Noto Sans";

	public static string Family
	{
		get
		{
			Register();
			return "Noto Sans";
		}
	}

	public static void Register()
	{
		lock (Gate)
		{
			if (_registered)
			{
				return;
			}
			Assembly executingAssembly = Assembly.GetExecutingAssembly();
			foreach (string item in from n in executingAssembly.GetManifestResourceNames()
				where n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
				select n)
			{
				using Stream stream = executingAssembly.GetManifestResourceStream(item);
				FontManager.RegisterFont(stream);
			}
			_registered = true;
		}
	}
}
