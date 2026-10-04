using System.Windows;
using System.Windows.Controls;

namespace PharmaBill.App.Controls;

public partial class GlobalMedicineSearchControl : UserControl
{
    public static readonly DependencyProperty ShowActionsProperty = DependencyProperty.Register(
        nameof(ShowActions), typeof(bool), typeof(GlobalMedicineSearchControl), new PropertyMetadata(false));

    public GlobalMedicineSearchControl() => InitializeComponent();

    // Stock and Purchases turn this on to get the stock actions, labels and empty-state hints.
    public bool ShowActions
    {
        get => (bool)GetValue(ShowActionsProperty);
        set => SetValue(ShowActionsProperty, value);
    }
}