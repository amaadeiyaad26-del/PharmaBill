using System.Windows;
using PharmaBill.App.ViewModels;
using PharmaBill.Sync.Terminals;

namespace PharmaBill.App;

/// <summary>First start on a PC with no records: the shop's main PC, or a billing counter for one.</summary>
public partial class WelcomeWindow : Window
{
	private readonly TerminalPairingViewModel _pairing;

	public WelcomeWindow(TerminalClientSettingsStore store)
	{
		InitializeComponent();
		_pairing = new TerminalPairingViewModel(store);
		_pairing.Paired += paired =>
		{
			PairedCounter = paired;
			DialogResult = true;
		};
		DataContext = _pairing;
	}

	/// <summary>Set when this PC was paired as a billing counter; null when it is the shop's main PC.</summary>
	public TerminalClientSettings? PairedCounter { get; private set; }

	private void MainPc_Click(object sender, RoutedEventArgs e) => DialogResult = true;

	private void Counter_Click(object sender, RoutedEventArgs e)
	{
		Choice.Visibility = Visibility.Collapsed;
		Pairing.Visibility = Visibility.Visible;
	}

	private void Back_Click(object sender, RoutedEventArgs e)
	{
		Pairing.Visibility = Visibility.Collapsed;
		Choice.Visibility = Visibility.Visible;
	}
}
