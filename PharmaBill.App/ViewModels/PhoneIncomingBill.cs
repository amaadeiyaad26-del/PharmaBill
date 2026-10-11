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
			}
		}
	}

	public string ChipText => "Bill " + Index + " (" + _status + ")";
}
