using PharmaBill.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PharmaBill.App;

/// <summary>One drug licence the buyer holds: its type (20B, 21B, 20 or 21), number and validity.</summary>
public sealed record DrugLicenceEntry(string LicenceType, string LicenceNumber, DateOnly IssuedOn, DateOnly ExpiresOn);

public partial class QuickAddRetailerWindow : Window
{
	private static readonly string[] LicenceTypes = { "20B", "21B", "20", "21" };

	private readonly List<LicenceBlock> _licenceBlocks = new List<LicenceBlock>();

	public string RetailerName { get; private set; } = string.Empty;
	public IReadOnlyList<DrugLicenceEntry> Licences { get; private set; } = Array.Empty<DrugLicenceEntry>();
	public string? Gstin { get; private set; }
	public string? Phone { get; private set; }
	public string? Address { get; private set; }
	public int CreditDays { get; private set; }

	public QuickAddRetailerWindow(string? suggestedName = null)
	{
		InitializeComponent();
		foreach (string type in LicenceTypes)
		{
			LicenceTypeBox.Items.Add(type);
		}
		NameBox.Text = suggestedName?.Trim() ?? string.Empty;
		NameBox.Focus();
		NameBox.CaretIndex = NameBox.Text.Length;
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}

	// Each DL type picked adds its own block below: number, issue date and expiry date.
	private void LicenceTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (LicenceTypeBox.SelectedItem is not string type)
		{
			return;
		}

		// Clear the choice so the same type can be picked again after its block is removed.
		LicenceTypeBox.SelectedIndex = -1;
		if (_licenceBlocks.Any((LicenceBlock block) => block.Type == type))
		{
			StatusText.Text = $"DL {type} is already added.";
			return;
		}

		StatusText.Text = string.Empty;
		AddLicenceBlock(type);
	}

	private void AddLicenceBlock(string type)
	{
		TextBox numberBox = new TextBox { Padding = new Thickness(10, 8, 10, 8) };
		DatePicker issuedPicker = new DatePicker { Padding = new Thickness(6), SelectedDate = BusinessClock.Now.Date };
		DatePicker expiresPicker = new DatePicker { Padding = new Thickness(6), SelectedDate = BusinessClock.Now.Date.AddYears(5) };

		Grid dates = new Grid();
		dates.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		dates.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		StackPanel issuedField = LabelledField("DL Issue Date *", issuedPicker, new Thickness(0, 0, 8, 0));
		StackPanel expiresField = LabelledField("DL Expiry Date *", expiresPicker, new Thickness(0));
		Grid.SetColumn(expiresField, 1);
		dates.Children.Add(issuedField);
		dates.Children.Add(expiresField);

		Button removeButton = new Button
		{
			Content = "Remove",
			Padding = new Thickness(10, 4, 10, 4),
			Style = TryFindResource("SecondaryButton") as Style
		};
		DockPanel header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 8) };
		DockPanel.SetDock(removeButton, Dock.Right);
		header.Children.Add(removeButton);
		header.Children.Add(new TextBlock
		{
			Text = $"Drug licence DL {type}",
			FontWeight = FontWeights.SemiBold,
			FontSize = 14,
			VerticalAlignment = VerticalAlignment.Center
		});

		StackPanel content = new StackPanel();
		content.Children.Add(header);
		content.Children.Add(LabelledField($"DL {type} Number *", numberBox, new Thickness(0, 0, 0, 10)));
		content.Children.Add(dates);

		Border host = new Border
		{
			Padding = new Thickness(12),
			Margin = new Thickness(0, 0, 0, 10),
			CornerRadius = new CornerRadius(8),
			BorderThickness = new Thickness(1),
			BorderBrush = TryFindResource("FieldBorderBrush") as Brush,
			Child = content
		};
		LicenceBlocks.Children.Add(host);

		LicenceBlock block = new LicenceBlock(type, numberBox, issuedPicker, expiresPicker);
		_licenceBlocks.Add(block);
		removeButton.Click += (object _, RoutedEventArgs _) =>
		{
			_licenceBlocks.Remove(block);
			LicenceBlocks.Children.Remove(host);
		};
		numberBox.Focus();
	}

	private static StackPanel LabelledField(string label, UIElement input, Thickness margin)
	{
		StackPanel field = new StackPanel { Margin = margin };
		field.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
		field.Children.Add(input);
		return field;
	}

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		string name = NameBox.Text?.Trim() ?? string.Empty;
		string gstin = GstinBox.Text?.Trim() ?? string.Empty;
		string phone = PhoneBox.Text?.Trim() ?? string.Empty;
		string address = AddressBox.Text?.Trim() ?? string.Empty;

		if (string.IsNullOrWhiteSpace(name))
		{
			StatusText.Text = "Business name is required.";
			return;
		}

		if (_licenceBlocks.Count == 0)
		{
			StatusText.Text = "Add at least one drug licence: pick its DL type above.";
			return;
		}

		List<DrugLicenceEntry> licences = new List<DrugLicenceEntry>();
		foreach (LicenceBlock block in _licenceBlocks)
		{
			string number = block.Number.Text?.Trim() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(number))
			{
				StatusText.Text = $"Enter the DL {block.Type} number.";
				block.Number.Focus();
				return;
			}

			if (!block.Issued.SelectedDate.HasValue || !block.Expires.SelectedDate.HasValue)
			{
				StatusText.Text = $"Enter the issue and expiry dates for DL {block.Type}.";
				return;
			}

			DateOnly issued = DateOnly.FromDateTime(block.Issued.SelectedDate.Value);
			DateOnly expires = DateOnly.FromDateTime(block.Expires.SelectedDate.Value);
			if (expires < issued)
			{
				StatusText.Text = $"DL {block.Type} expiry cannot be earlier than its issue date.";
				return;
			}

			licences.Add(new DrugLicenceEntry(block.Type, number, issued, expires));
		}

		if (!string.IsNullOrWhiteSpace(gstin) && gstin.Length != 15)
		{
			StatusText.Text = "GSTIN must be exactly 15 characters when provided.";
			return;
		}

		if (!int.TryParse(CreditDaysBox.Text?.Trim(), out int creditDays) || creditDays < 0)
		{
			StatusText.Text = "Credit period must be zero or a positive number of days.";
			return;
		}

		RetailerName = name;
		Licences = licences;
		Gstin = string.IsNullOrWhiteSpace(gstin) ? null : gstin.ToUpperInvariant();
		Phone = string.IsNullOrWhiteSpace(phone) ? null : phone;
		Address = string.IsNullOrWhiteSpace(address) ? null : address;
		CreditDays = creditDays;
		DialogResult = true;
		Close();
	}

	private sealed record LicenceBlock(string Type, TextBox Number, DatePicker Issued, DatePicker Expires);
}
