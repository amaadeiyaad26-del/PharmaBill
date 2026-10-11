using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PharmaBill.App.ViewModels;

public sealed class PhoneBillLineSnapshot
{
	public string MedicineName { get; set; } = string.Empty;

	public Guid? DrugId { get; set; }

	public string Batch { get; set; } = string.Empty;

	public string Expiry { get; set; } = string.Empty;

	public decimal Quantity { get; set; }

	public decimal Free { get; set; }

	public decimal Mrp { get; set; }

	public decimal Rate { get; set; }

	public decimal Gst { get; set; }

	public decimal Discount { get; set; }
}

public sealed class PhoneIncomingBill : ObservableObject
{
	private string _status = "Received";

	public int Index { get; init; }

	public string SupplierName { get; set; } = string.Empty;

	public string InvoiceNo { get; set; } = string.Empty;

	public DateTime InvoiceDate { get; set; } = DateTime.Today;

	public string? FilePath { get; set; }

	public string? OriginalFileName { get; set; }

	public List<PhoneBillLineSnapshot> Lines { get; } = new List<PhoneBillLineSnapshot>();

	public bool IsCommitted => string.Equals(_status, "Committed", StringComparison.Ordinal);

	public bool IsExtracting => string.Equals(_status, "Extracting", StringComparison.OrdinalIgnoreCase);

	public string Status
	{
		get
		{
			return _status;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_status, value))
			{
				_status = value;
				OnPropertyChanged(nameof(Status));
				OnPropertyChanged(nameof(ChipText));
				OnPropertyChanged(nameof(IsCommitted));
				OnPropertyChanged(nameof(IsExtracting));
			}
		}
	}

	/// <summary>Call after Lines is filled so chip text refreshes with the item count.</summary>
	public void NotifyLinesChanged()
	{
		OnPropertyChanged(nameof(ChipText));
	}

	public string ChipText
	{
		get
		{
			if (IsExtracting)
			{
				return "Extracting Bill " + Index + "...";
			}

			if (IsCommitted)
			{
				return "Bill " + Index + " (Committed)";
			}

			if (Lines.Count > 0)
			{
				string label = string.Equals(_status, "Loaded", StringComparison.OrdinalIgnoreCase)
					? "Loaded"
					: "Extracted";
				return "Bill " + Index + " (" + label + " - " + Lines.Count + " items)";
			}

			return "Bill " + Index + " (" + _status + ")";
		}
	}
}
