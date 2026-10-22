using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.Sync.Terminals;

namespace PharmaBill.App.ViewModels;

public sealed record DiscoveredServerRow(string Name, string Host, int Port, string Label);

/// <summary>Pairs this PC with the shop's server as a billing counter: find or type the server, type its code.</summary>
public sealed class TerminalPairingViewModel : ObservableObject
{
	private readonly TerminalClientSettingsStore _store;

	private string _serverAddress = string.Empty;
	private string _codeInput = string.Empty;
	private string _terminalName = Environment.MachineName;
	private DiscoveredServerRow? _selectedServer;
	private bool _isBusy;
	private string _statusMessage = string.Empty;
	private string _errorMessage = string.Empty;

	public TerminalPairingViewModel(TerminalClientSettingsStore store)
	{
		_store = store;
		FindServersCommand = new AsyncRelayCommand(FindServersAsync);
		PairCommand = new AsyncRelayCommand(PairAsync);
	}

	/// <summary>Raised on the UI thread after this PC is paired and the pairing is saved.</summary>
	public event Action<TerminalClientSettings>? Paired;

	/// <summary>Refuses to pair, with a reason, when this PC must not become a counter (it serves counters itself).</summary>
	public Func<string?>? Blocker { get; set; }

	public ObservableCollection<DiscoveredServerRow> DiscoveredServers { get; } = new ObservableCollection<DiscoveredServerRow>();

	public DiscoveredServerRow? SelectedServer
	{
		get => _selectedServer;
		set
		{
			if (SetProperty(ref _selectedServer, value) && value != null)
			{
				ServerAddress = value.Host;
			}
		}
	}

	public string ServerAddress
	{
		get => _serverAddress;
		set => SetProperty(ref _serverAddress, value);
	}

	public string CodeInput
	{
		get => _codeInput;
		set => SetProperty(ref _codeInput, value);
	}

	public string TerminalName
	{
		get => _terminalName;
		set => SetProperty(ref _terminalName, value);
	}

	public bool IsBusy
	{
		get => _isBusy;
		private set => SetProperty(ref _isBusy, value);
	}

	public string StatusMessage
	{
		get => _statusMessage;
		private set => SetProperty(ref _statusMessage, value);
	}

	public string ErrorMessage
	{
		get => _errorMessage;
		private set => SetProperty(ref _errorMessage, value);
	}

	public IAsyncRelayCommand FindServersCommand { get; }

	public IAsyncRelayCommand PairCommand { get; }

	private async Task FindServersAsync()
	{
		ErrorMessage = string.Empty;
		IsBusy = true;
		StatusMessage = "Looking for the shop's server on this network…";
		try
		{
			var found = await TerminalDiscovery.FindAsync(TimeSpan.FromSeconds(3));
			DiscoveredServers.Clear();
			foreach (DiscoveredTerminalServer server in found)
			{
				DiscoveredServers.Add(new DiscoveredServerRow(server.Name, server.Host, server.Port, $"{server.Name}  ({server.Host})"));
			}
			SelectedServer = DiscoveredServers.FirstOrDefault();
			StatusMessage = found.Count == 0
				? "No server found automatically. Type the server PC's address (shown on its Devices panel)."
				: $"Found {found.Count} server{(found.Count == 1 ? string.Empty : "s")}.";
		}
		catch (Exception ex)
		{
			StatusMessage = "Automatic search is not available here. Type the server PC's address.";
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
		finally
		{
			IsBusy = false;
		}
	}

	private async Task PairAsync()
	{
		ErrorMessage = string.Empty;
		if (Blocker?.Invoke() is string blocked)
		{
			ErrorMessage = blocked;
			return;
		}
		(string host, int port) = ParseAddress(ServerAddress, SelectedServer);
		if (string.IsNullOrWhiteSpace(host))
		{
			ErrorMessage = "Choose the server or type its address.";
			return;
		}
		IsBusy = true;
		StatusMessage = "Pairing…";
		try
		{
			TerminalClientSettings paired = await TerminalPairingClient.PairAsync(host, port, CodeInput, TerminalName);
			_store.Save(paired);
			CodeInput = string.Empty;
			StatusMessage = $"Paired with {paired.ShopName} on {paired.ServerName}.";
			Paired?.Invoke(paired);
		}
		catch (TerminalPairingException ex)
		{
			StatusMessage = string.Empty;
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
		finally
		{
			IsBusy = false;
		}
	}

	internal static (string Host, int Port) ParseAddress(string typed, DiscoveredServerRow? selected)
	{
		string text = (typed ?? string.Empty).Trim();
		if (selected != null && string.Equals(text, selected.Host, StringComparison.OrdinalIgnoreCase))
		{
			return (selected.Host, selected.Port);
		}
		int colon = text.LastIndexOf(':');
		if (colon > 0 && int.TryParse(text[(colon + 1)..], out int port))
		{
			return (text[..colon], port);
		}
		return (text, TerminalProtocol.DefaultPort);
	}
}
