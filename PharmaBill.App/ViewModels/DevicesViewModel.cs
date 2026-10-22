using PharmaBill.Core;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;
using PharmaBill.Core.Security;
using PharmaBill.Sync;
using PharmaBill.Sync.Terminals;

namespace PharmaBill.App.ViewModels;

public sealed record PairedCounterRow(Guid Id, string Name, string PairedAt, string LastSeen);

public sealed record DiscoveredServerRow(string Name, string Host, int Port, string Label);

// Sync Station > Devices: every PC connected to this shop's data, in one place. Billing counters reach one
// database on the server PC; phones (shown separately on this page) keep their own copy and sync later.
public sealed class DevicesViewModel : ObservableObject
{
	private readonly TerminalServer _server;
	private readonly TerminalServerSettingsStore _serverSettings;
	private readonly TerminalDeviceStore _devices;
	private readonly TerminalPairing _pairing;
	private readonly TerminalClientSettingsStore _clientSettings;
	private readonly CurrentSession _session;
	private readonly IConfirmationService _confirmation;
	private readonly DispatcherTimer _countdown;

	private bool _isServing;
	private bool _isBusy;
	private string _serverStatus = string.Empty;
	private string _firewallStatus = string.Empty;
	private TerminalPairingCode? _openCode;
	private string _pairingCountdown = string.Empty;
	private string _pairedServerSummary = string.Empty;
	private string _serverAddress = string.Empty;
	private string _codeInput = string.Empty;
	private string _terminalName = Environment.MachineName;
	private DiscoveredServerRow? _selectedServer;
	private string _statusMessage = string.Empty;
	private string _errorMessage = string.Empty;

	public DevicesViewModel(TerminalServer server, TerminalServerSettingsStore serverSettings, TerminalDeviceStore devices, TerminalPairing pairing, TerminalClientSettingsStore clientSettings, CurrentSession session, IConfirmationService confirmation)
	{
		_server = server;
		_serverSettings = serverSettings;
		_devices = devices;
		_pairing = pairing;
		_clientSettings = clientSettings;
		_session = session;
		_confirmation = confirmation;
		_countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
		_countdown.Tick += (_, _) => RefreshPairingCode();
		_devices.Changed += () => OnUi(RefreshCounters);
		_pairing.Closed += device => OnUi(() =>
		{
			RefreshPairingCode();
			if (device != null)
			{
				StatusMessage = $"\"{device.Name}\" is now a billing counter for this shop.";
			}
		});
		_clientSettings.Changed += _ => OnUi(RefreshClient);
		SetServingCommand = new AsyncRelayCommand<bool>(SetServingAsync);
		AddCounterCommand = new RelayCommand(AddCounter);
		CancelPairingCommand = new RelayCommand(() => _pairing.Cancel());
		RemoveCounterCommand = new RelayCommand<PairedCounterRow>(RemoveCounter);
		AllowFirewallCommand = new AsyncRelayCommand(AllowFirewallAsync);
		FindServersCommand = new AsyncRelayCommand(FindServersAsync);
		PairCommand = new AsyncRelayCommand(PairAsync);
		ForgetServerCommand = new RelayCommand(ForgetServer);
		_isServing = _serverSettings.Load().Enabled;
		Refresh();
	}

	// ---- This PC serves billing counters ----

	public bool CanManage => _session.User != null && PermissionMatrix.Allows(_session.User.Role, AppPermission.ManageSettings);

	public bool IsServing
	{
		get => _isServing;
		set
		{
			if (value != _isServing)
			{
				SetServingCommand.Execute(value);
			}
		}
	}

	public string ServerStatus
	{
		get => _serverStatus;
		private set => SetProperty(ref _serverStatus, value);
	}

	public string FirewallStatus
	{
		get => _firewallStatus;
		private set => SetProperty(ref _firewallStatus, value);
	}

	public ObservableCollection<PairedCounterRow> Counters { get; } = new ObservableCollection<PairedCounterRow>();

	public bool HasCounters => Counters.Count > 0;

	public bool IsPairingOpen => _openCode != null;

	/// <summary>The code split for reading aloud and typing: "K7Q 4MX".</summary>
	public string PairingCode => _openCode == null ? string.Empty : _openCode.Code[..3] + " " + _openCode.Code[3..];

	public string PairingCountdown
	{
		get => _pairingCountdown;
		private set => SetProperty(ref _pairingCountdown, value);
	}

	// ---- This PC is a billing counter ----

	public bool IsPairedAsCounter => _clientSettings.Load() != null;

	public bool CanPairAsCounter => !IsPairedAsCounter && !_isServing;

	/// <summary>A PC is either the server or a counter, never both.</summary>
	public bool CanToggleServing => CanManage && !IsPairedAsCounter;

	public bool ShowServingNote => _isServing && !IsPairedAsCounter;

	public string PairedServerSummary
	{
		get => _pairedServerSummary;
		private set => SetProperty(ref _pairedServerSummary, value);
	}

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

	public IAsyncRelayCommand<bool> SetServingCommand { get; }

	public IRelayCommand AddCounterCommand { get; }

	public IRelayCommand CancelPairingCommand { get; }

	public IRelayCommand<PairedCounterRow> RemoveCounterCommand { get; }

	public IAsyncRelayCommand AllowFirewallCommand { get; }

	public IAsyncRelayCommand FindServersCommand { get; }

	public IAsyncRelayCommand PairCommand { get; }

	public IRelayCommand ForgetServerCommand { get; }

	public void Refresh()
	{
		OnPropertyChanged(nameof(CanManage));
		RefreshServer();
		RefreshCounters();
		RefreshPairingCode();
		RefreshClient();
	}

	private async Task SetServingAsync(bool serve)
	{
		ErrorMessage = string.Empty;
		if (!CanManage)
		{
			ErrorMessage = "Only the owner or a manager can change this.";
			OnPropertyChanged(nameof(IsServing));
			return;
		}
		if (serve && IsPairedAsCounter)
		{
			ErrorMessage = "This PC is a billing counter for another server. Forget that pairing first.";
			OnPropertyChanged(nameof(IsServing));
			return;
		}
		IsBusy = true;
		try
		{
			TerminalServerSettings settings = _serverSettings.Load();
			if (serve)
			{
				await _server.StartAsync(settings.Port);
			}
			else
			{
				_pairing.Cancel();
				await _server.StopAsync();
			}
			_serverSettings.Save(settings with { Enabled = serve });
			_isServing = serve;
			StatusMessage = serve ? "This PC now serves billing counters on the shop network." : "Billing counters are switched off. Paired PCs stay paired.";
		}
		catch (Exception ex)
		{
			ErrorMessage = "Could not start serving billing counters: " + ex.Message;
		}
		finally
		{
			IsBusy = false;
			OnPropertyChanged(nameof(IsServing));
			OnPropertyChanged(nameof(CanPairAsCounter));
			OnPropertyChanged(nameof(ShowServingNote));
			RefreshServer();
		}
	}

	private void AddCounter()
	{
		ErrorMessage = string.Empty;
		if (!CanManage || !_server.IsRunning)
		{
			ErrorMessage = !CanManage ? "Only the owner or a manager can add a billing counter." : "Switch on \"Serve billing counters\" first.";
			return;
		}
		_pairing.Open();
		_countdown.Start();
		RefreshPairingCode();
	}

	private void RemoveCounter(PairedCounterRow? row)
	{
		if (row == null || !CanManage)
		{
			return;
		}
		if (_confirmation.Confirm($"Remove \"{row.Name}\"? It stops working at once and must be paired again to bill.", "Remove billing counter"))
		{
			_server.Revoke(row.Id);
			StatusMessage = $"\"{row.Name}\" was removed. Its next request is refused.";
		}
	}

	private async Task AllowFirewallAsync()
	{
		int port = _server.IsRunning ? _server.Port : _serverSettings.Load().Port;
		LanSyncFirewallHelper.FirewallCheckResult result = await Task.Run(() => LanSyncFirewallHelper.AllowTerminalRuleElevated(port));
		FirewallStatus = result.Message + (string.IsNullOrWhiteSpace(result.Detail) ? string.Empty : " — " + result.Detail);
	}

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
			ErrorMessage = ex.Message;
		}
		finally
		{
			IsBusy = false;
		}
	}

	private async Task PairAsync()
	{
		ErrorMessage = string.Empty;
		if (_isServing)
		{
			ErrorMessage = "This PC serves billing counters itself, so it cannot be a counter for another server.";
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
			_clientSettings.Save(paired);
			CodeInput = string.Empty;
			StatusMessage = $"Paired with {paired.ShopName} on {paired.ServerName}.";
		}
		catch (TerminalPairingException ex)
		{
			StatusMessage = string.Empty;
			ErrorMessage = ex.Message;
		}
		finally
		{
			IsBusy = false;
			RefreshClient();
		}
	}

	private void ForgetServer()
	{
		TerminalClientSettings? paired = _clientSettings.Load();
		if (paired != null && _confirmation.Confirm($"Forget the pairing with {paired.ShopName} on {paired.ServerName}? To use this PC as a counter again, pair it again with a new code.", "Forget server"))
		{
			_clientSettings.Clear();
			StatusMessage = "This PC is no longer a billing counter. Also remove it from the server's Devices panel.";
		}
	}

	private void RefreshServer()
	{
		if (_server.IsRunning)
		{
			IPAddress? lan = _server.BoundAddresses.FirstOrDefault(address => !IPAddress.IsLoopback(address));
			string fingerprint = _server.Fingerprint;
			ServerStatus = (lan == null ? "Serving, but no shop network was found: counters cannot reach this PC." : $"Serving billing counters at {lan}:{_server.Port}.")
				+ $"  Server check: {fingerprint[..4]} {fingerprint[4..8]} {fingerprint[8..12]}";
			LanSyncFirewallHelper.FirewallCheckResult firewall = LanSyncFirewallHelper.CheckTerminalRule(_server.Port);
			FirewallStatus = firewall.Message;
		}
		else
		{
			ServerStatus = _isServing ? "Switched on, but not running. Switch it off and on again, or restart Pharma Remit." : "Off. This PC is not serving billing counters.";
			FirewallStatus = string.Empty;
		}
	}

	private void RefreshCounters()
	{
		Counters.Clear();
		foreach (TerminalDevice device in _devices.List().Where(device => device.IsActive))
		{
			Counters.Add(new PairedCounterRow(device.Id, device.Name, device.PairedAtUtc.ToBusinessLocal().ToString("d MMM yyyy, h:mm tt"),
				device.LastSeenAtUtc is DateTime seen ? "last seen " + seen.ToBusinessLocal().ToString("d MMM, h:mm tt") : "not connected yet"));
		}
		OnPropertyChanged(nameof(HasCounters));
	}

	private void RefreshPairingCode()
	{
		_openCode = _pairing.Current;
		if (_openCode == null)
		{
			_countdown.Stop();
			PairingCountdown = string.Empty;
		}
		else
		{
			TimeSpan left = _openCode.ExpiresAtUtc - BusinessClock.UtcNow;
			PairingCountdown = $"Valid for {Math.Max(0, (int)left.TotalMinutes)}:{Math.Max(0, left.Seconds):00} more. Type it on the new PC.";
		}
		OnPropertyChanged(nameof(IsPairingOpen));
		OnPropertyChanged(nameof(PairingCode));
	}

	private void RefreshClient()
	{
		TerminalClientSettings? paired = _clientSettings.Load();
		PairedServerSummary = paired == null
			? string.Empty
			: $"This PC is billing counter \"{paired.TerminalName}\" for {paired.ShopName} on {paired.ServerName} ({paired.ServerHost}), paired {paired.PairedAtUtc.ToBusinessLocal():d MMM yyyy}. "
				+ $"Server check: {paired.ServerFingerprint[..4]} {paired.ServerFingerprint[4..8]} {paired.ServerFingerprint[8..12]} (the same as on the server's screen). Billing from here arrives with the next update.";
		OnPropertyChanged(nameof(IsPairedAsCounter));
		OnPropertyChanged(nameof(CanPairAsCounter));
		OnPropertyChanged(nameof(CanToggleServing));
		OnPropertyChanged(nameof(ShowServingNote));
	}

	private static (string Host, int Port) ParseAddress(string typed, DiscoveredServerRow? selected)
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

	private static void OnUi(Action action)
	{
		Dispatcher? dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			action();
		}
		else
		{
			dispatcher.BeginInvoke(action);
		}
	}
}
