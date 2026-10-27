using System.Windows;
using PharmaBill.Data.Services;

namespace PharmaBill.App;

/// <summary>
/// Shown when stock entry would change a medicine's statutory schedule: says what changes, takes the reason, and hands
/// over to the Admin PIN prompt. Nothing is saved here.
/// </summary>
public partial class ScheduleOverrideWindow : Window
{
	public ScheduleOverrideWindow(ScheduleChangeRequiredException block)
	{
		InitializeComponent();
		MessageText.Text = $"This medication is subject to statutory schedule rules (Schedule {block.SavedSchedule}/{block.RequestedSchedule}). An override is required to proceed.";
		MedicineText.Text = block.MedicineName;
		ChangeText.Text = $"Saved as schedule {block.SavedSchedule}. This stock entry chose schedule {block.RequestedSchedule}.";
		if (block.LowersControls)
		{
			LowersControlsWarning.Visibility = Visibility.Visible;
			LowersControlsText.Text = $"Schedule {block.RequestedSchedule} is less controlled than {block.SavedSchedule}: bills for this medicine will stop asking for what schedule {block.SavedSchedule} requires. Only override if the pack label or the supplier invoice shows schedule {block.RequestedSchedule}.";
		}
		Loaded += (_, _) => ReasonBox.Focus();
	}

	public string Reason => ReasonBox.Text.Trim();

	private void Override_Click(object sender, RoutedEventArgs e)
	{
		if (Reason.Length == 0)
		{
			ErrorText.Text = "Give a reason, for example \"Pack label shows Schedule H1\".";
			ReasonBox.Focus();
			return;
		}
		DialogResult = true;
	}
}
