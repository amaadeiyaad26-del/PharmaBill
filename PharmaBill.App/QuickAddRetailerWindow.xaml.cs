using System;
using System.Windows;
using System.Windows.Controls;

namespace PharmaBill.App;

public partial class QuickAddRetailerWindow : Window
{
	public string RetailerName { get; private set; } = string.Empty;
	public string LicenceType { get; private set; } = "20B";
	public string LicenceNumber { get; private set; } = string.Empty;
	public DateOnly IssuedOn { get; private set; }
	public DateOnly ExpiresOn { get; private set; }
	public string? Gstin { get; private set; }
	public string? Phone { get; private set; }
	public string? Address { get; private set; }
	public int CreditDays { get; private set; }

	public QuickAddRetailerWindow(string? suggestedName = null)
	{
		InitializeComponent();
		NameBox.Text = suggestedName?.Trim() ?? string.Empty;
		IssuedOnPicker.SelectedDate = DateTime.Today;
		ExpiresOnPicker.SelectedDate = DateTime.Today.AddYears(5);
		NameBox.Focus();
		NameBox.CaretIndex = NameBox.Text.Length;
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		string name = NameBox.Text?.Trim() ?? string.Empty;
		string licenceNumber = LicenceNumberBox.Text?.Trim() ?? string.Empty;
		string licenceType = (LicenceTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Trim()
			?? LicenceTypeBox.Text?.Trim()
			?? "20B";
		string gstin = GstinBox.Text?.Trim() ?? string.Empty;
		string phone = PhoneBox.Text?.Trim() ?? string.Empty;
		string address = AddressBox.Text?.Trim() ?? string.Empty;

		if (string.IsNullOrWhiteSpace(name))
		{
			StatusText.Text = "Business name is required.";
			return;
		}

		if (string.IsNullOrWhiteSpace(licenceNumber))
		{
			StatusText.Text = "Drug License (DL) Number is required for wholesale buyers.";
			return;
		}

		if (!IssuedOnPicker.SelectedDate.HasValue || !ExpiresOnPicker.SelectedDate.HasValue)
		{
			StatusText.Text = "DL issue and expiry dates are required.";
			return;
		}

		DateOnly issued = DateOnly.FromDateTime(IssuedOnPicker.SelectedDate.Value);
		DateOnly expires = DateOnly.FromDateTime(ExpiresOnPicker.SelectedDate.Value);
		if (expires < issued)
		{
			StatusText.Text = "DL expiry cannot be earlier than the issue date.";
			return;
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
		LicenceType = licenceType;
		LicenceNumber = licenceNumber;
		IssuedOn = issued;
		ExpiresOn = expires;
		Gstin = string.IsNullOrWhiteSpace(gstin) ? null : gstin.ToUpperInvariant();
		Phone = string.IsNullOrWhiteSpace(phone) ? null : phone;
		Address = string.IsNullOrWhiteSpace(address) ? null : address;
		CreditDays = creditDays;
		DialogResult = true;
		Close();
	}
}