using System;
using PharmaBill.Core.Entities;

namespace PharmaBill.Data.Services;

/// <summary>The supplier's own bill (scan, photo or PDF) kept with a purchase, for opening from Reports or Purchase history.</summary>
public sealed record PurchaseBillAttachment(Guid PurchaseId, string BillNo, string Supplier, string? Path);

public static class PurchaseAttachmentPaths
{
	/// <summary>The attached file: the recorded path, or, for bills saved by older builds, the one written into the notes.</summary>
	public static string? For(PurchaseInvoice invoice) =>
		string.IsNullOrWhiteSpace(invoice.AttachedInvoicePath) ? FromLegacyNotes(invoice.Notes) : invoice.AttachedInvoicePath;

	/// <summary>Older builds wrote "Invoice image: &lt;path&gt;" into the purchase notes instead of a field.</summary>
	public static string? FromLegacyNotes(string? notes)
	{
		if (string.IsNullOrWhiteSpace(notes))
		{
			return null;
		}

		const string marker = "Invoice image:";
		int index = notes.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
		if (index < 0)
		{
			return null;
		}

		string path = notes[(index + marker.Length)..].Trim();
		int lineBreak = path.IndexOfAny(new[] { '\r', '\n' });
		if (lineBreak >= 0)
		{
			path = path[..lineBreak].Trim();
		}

		return path.Length == 0 ? null : path;
	}
}
