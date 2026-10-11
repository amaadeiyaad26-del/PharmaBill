using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using PharmaBill.Data.Services;

namespace PharmaBill.App;

public partial class PatientHistoryWindow : Window
{
	private readonly Action<RetailPatientBill> _copy;

	public PatientHistoryWindow(string patientName, string phone, IReadOnlyList<RetailPatientBill> bills, Action<RetailPatientBill> copy)
	{
		InitializeComponent();
		_copy = copy;
		HeaderText.Text = string.IsNullOrWhiteSpace(patientName) ? phone : patientName + "  ·  " + phone;
		BillsGrid.ItemsSource = bills;
		if (bills.Count > 0)
		{
			BillsGrid.SelectedIndex = 0;
		}
	}

	private void Bills_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		RetailPatientBill? bill = BillsGrid.SelectedItem as RetailPatientBill;
		LinesGrid.ItemsSource = bill?.Lines;
		CopyButton.IsEnabled = bill != null;
	}

	private void Copy_Click(object sender, RoutedEventArgs e)
	{
		if (BillsGrid.SelectedItem is RetailPatientBill bill)
		{
			_copy(bill);
			StatusText.Text = "Medicines from " + bill.InvoiceNo + " copied to the bill.";
		}
	}
}
