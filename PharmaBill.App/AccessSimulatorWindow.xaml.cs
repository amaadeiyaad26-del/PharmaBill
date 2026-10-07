using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using PharmaBill.Core.Security;

namespace PharmaBill.App;

public partial class AccessSimulatorWindow : Window
{
	public IReadOnlyList<EntitlementStatus> Statuses { get; } = Enum.GetValues<EntitlementStatus>();

	public AccessSimulatorWindow()
	{
		InitializeComponent();
		DataContext = this;
		UpdatePreview();
	}

	private void StatusPicker_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		UpdatePreview();
	}

	private void UpdatePreview()
	{
		if (StatusPicker.SelectedItem is EntitlementStatus entitlementStatus)
		{
			bool flag = entitlementStatus == EntitlementStatus.EXPIRED;
			ResultText.Text = (flag ? "READ-ONLY: only view, search, print, export and backup." : $"{entitlementStatus}: write operations are enabled in the simulator preview.");
		}
	}
}
