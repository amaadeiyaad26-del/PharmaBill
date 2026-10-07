using System;
using System.Collections.Generic;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PharmaBill.App.Services;

public static class UserManualPdfBuilder
{
	public const string FileName = "PharmaBill_User_Manual.pdf";

	public static string EnsurePdfPath()
	{
		foreach (string item in EnumerateBundledCandidates())
		{
			if (File.Exists(item) && new FileInfo(item).Length > 500)
			{
				return item;
			}
		}
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "docs");
		Directory.CreateDirectory(text);
		string text2 = Path.Combine(text, "PharmaBill_User_Manual.pdf");
		Generate(text2);
		return text2;
	}

	public static void Generate(string destinationPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath, "destinationPath");
		Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
		Document.Create((IDocumentContainer container) =>
		{
			container.Page((PageDescriptor page) =>
			{
				page.Size(PageSizes.A4);
				page.Margin(40f);
				page.DefaultTextStyle((TextStyle style) => style.FontFamily(PdfFonts.Family).FontSize(11f));
				page.Header().Column((ColumnDescriptor header) =>
				{
					header.Item().Text("PharmaBill User Manual & Quick Start Guide").FontSize(20f)
						.Bold();
					header.Item().Text("Designed and developed by S.A.E.R. to serve pharma professionals.").FontSize(9f)
						.FontColor(Colors.Grey.Darken1);
				});
				page.Content().PaddingVertical(12f).Column((ColumnDescriptor column) =>
				{
					column.Spacing(10f);
					column.Item().Text("1. Welcome").Bold()
						.FontSize(14f);
					column.Item().Text("PharmaBill is an offline-first pharmacy billing system for India (Retail Chemist, Wholesaler, or Both). This guide covers first-day setup for desktop, Android LAN sync, backups, stock, and GST invoicing.");
					column.Item().Text("2. Connecting Local Wi-Fi Sync (Port 5055)").Bold()
						.FontSize(14f);
					column.Item().Text("• Connect the Windows PC and Android phone to the same Wi-Fi network.\n• Open Sync → Offline device sync. Confirm Listener shows Listening and note the PC Wi-Fi IP and Port 5055.\n• Scan the QR code in the Android app, or wait for mDNS discovery (_pharmabill-sync._tcp).\n• If pairing fails: allow PharmaBill through Windows Firewall (Private networks) or run scripts/allow-firewall-port5055.ps1 as Administrator. When LAN is blocked, encrypted change logs fall back to Google Drive PharmaBill_Sync automatically.");
					column.Item().Text("3. Google Drive automated backups").Bold()
						.FontSize(14f);
					column.Item().Text("• In Sync → Configure Keys, paste your Google Desktop OAuth client ID and secret.\n• Tap Link Google Account, then enable Sync on Exit and/or Sync on Bill Save.\n• Encrypted backups land in the PharmaBill_Sync folder on Drive. Watch the in-card progress bar for real upload percentage and Retry Now on rate limits.");
					column.Item().Text("4. Drug catalog, FEFO batches & GST invoicing").Bold()
						.FontSize(14f);
					column.Item().Text("• Import or search the medicine catalogue; stock quantities are movement-derived.\n• Batches are picked FEFO (earliest expiry first). Never sell expired stock; near-expiry sales need confirmation.\n• Retail: patient billing with schedule registers. Wholesale: tax invoices only to Active buyers with valid Form 20/21 (or wholesale) licences. Trade rate must not exceed MRP. Free qty deducts stock with zero taxable value.\n• Record transport/vehicle details for e-way readiness; IRN/e-way numbers are entered manually.");
					column.Item().Text("5. Support").Bold()
						.FontSize(14f);
					column.Item().Text("Designed and developed by S.A.E.R. to serve pharma professionals. For any query: email at pharma.bill26@gmail.com");
				});
				page.Footer().AlignCenter().Text((TextDescriptor text) =>
				{
					text.Span("PharmaBill  ·  ").FontSize(9f);
					text.Span("pharma.bill26@gmail.com").FontSize(9f);
				});
			});
		}).GeneratePdf(destinationPath);
	}

	private static IEnumerable<string> EnumerateBundledCandidates()
	{
		string baseDir = AppContext.BaseDirectory;
		yield return Path.Combine(baseDir, "Assets", "Docs", "PharmaBill_User_Manual.pdf");
		yield return Path.Combine(baseDir, "docs", "PharmaBill_User_Manual.pdf");
		yield return Path.Combine(baseDir, "PharmaBill_User_Manual.pdf");
		string asmDir = Path.GetDirectoryName(typeof(UserManualPdfBuilder).Assembly.Location);
		if (!string.IsNullOrWhiteSpace(asmDir))
		{
			yield return Path.Combine(asmDir, "Assets", "Docs", "PharmaBill_User_Manual.pdf");
			yield return Path.Combine(asmDir, "docs", "PharmaBill_User_Manual.pdf");
		}
	}

	public static string? TryGetMarkdownManualPath()
	{
		string baseDirectory = AppContext.BaseDirectory;
		string[] array = new string[2]
		{
			Path.Combine(baseDirectory, "docs", "PharmaBill_User_Manual.md"),
			Path.Combine(baseDirectory, "Assets", "Docs", "PharmaBill_User_Manual.md")
		};
		foreach (string text in array)
		{
			if (File.Exists(text))
			{
				return text;
			}
		}
		return null;
	}

	public static string? TryGetQaChecklistPath()
	{
		string baseDirectory = AppContext.BaseDirectory;
		string[] array = new string[2]
		{
			Path.Combine(baseDirectory, "docs", "Pre_Release_QA_Checklist.md"),
			Path.Combine(baseDirectory, "Assets", "Docs", "Pre_Release_QA_Checklist.md")
		};
		foreach (string text in array)
		{
			if (File.Exists(text))
			{
				return text;
			}
		}
		return null;
	}
}
