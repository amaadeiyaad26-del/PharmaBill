using System.Windows;
using PharmaBill.App.Services;

namespace PharmaBill.App;

public partial class LicenceStatusWindow : Window
{
	public LicenceStatusWindow(LicenceSummary summary)
	{
		InitializeComponent();
		DataContext = summary;
		int? trialDaysLeft = summary.TrialDaysLeft;
		if (trialDaysLeft.HasValue)
		{
			int valueOrDefault = trialDaysLeft.GetValueOrDefault();
			string value = ((valueOrDefault == 1) ? "1 day left" : $"{valueOrDefault} days left");
			DaysText.Text = $"Trial: {value} of {summary.TrialDaysTotal}";
			DaysBar.Maximum = summary.TrialDaysTotal;
			DaysBar.Value = valueOrDefault;
		}
		else
		{
			TrialPanel.Visibility = Visibility.Collapsed;
		}
	}
}
