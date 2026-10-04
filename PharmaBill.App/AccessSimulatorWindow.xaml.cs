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

    private void StatusPicker_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (StatusPicker.SelectedItem is not EntitlementStatus status)
        {
            return;
        }

        var readOnly = status == EntitlementStatus.EXPIRED;
        ResultText.Text = readOnly
            ? "READ-ONLY: only view, search, print, export and backup."
            : $"{status}: write operations are enabled in the simulator preview.";
    }
}
