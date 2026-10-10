using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace PharmaBill.App.Services;

public sealed class SerialScannerService : IScannerService, IDisposable
{
	private readonly object _gate = new object();
	private readonly StringBuilder _buffer = new StringBuilder();
	private readonly Timer _reconnect;
	private SerialPort? _port;
	private bool _enabled;
	private bool _disposed;

	public SerialScannerService()
	{
		_reconnect = new Timer(_ => TryOpen(), null, Timeout.Infinite, Timeout.Infinite);
	}

	public event EventHandler<string>? BarcodeReceived;

	public bool IsListening { get; private set; }

	public string Status { get; private set; } = "Scanner idle.";

	public void Start()
	{
		_enabled = true;
		TryOpen();
		_reconnect.Change(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
	}

	public void Stop()
	{
		_enabled = false;
		_reconnect.Change(Timeout.Infinite, Timeout.Infinite);
		ClosePort();
		IsListening = false;
		Status = "Scanner disconnected.";
	}

	public void Toggle()
	{
		if (IsListening)
		{
			Stop();
		}
		else
		{
			Start();
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Stop();
		_reconnect.Dispose();
	}

	private void TryOpen()
	{
		if (_disposed || !_enabled)
		{
			return;
		}

		lock (_gate)
		{
			if (_port?.IsOpen == true)
			{
				IsListening = true;
				return;
			}

			ClosePort();
			string? name = ReadConfiguredPort();
			if (string.IsNullOrWhiteSpace(name))
			{
				try
				{
					string[] names = SerialPort.GetPortNames();
					name = names.Length == 0 ? null : names[0];
				}
				catch (Exception ex)
				{
					Status = "COM ports could not be listed: " + ex.Message;
					IsListening = false;
					return;
				}
			}

			if (string.IsNullOrWhiteSpace(name))
			{
				Status = "No COM port found. Save the port name in scanner-com.txt, or use a USB keyboard-wedge scanner.";
				IsListening = false;
				return;
			}

			try
			{
				SerialPort port = new SerialPort(name, 9600, Parity.None, 8, StopBits.One)
				{
					Encoding = Encoding.ASCII,
					NewLine = "\r",
					DtrEnable = true,
					RtsEnable = true
				};
				port.DataReceived += OnDataReceived;
				port.ErrorReceived += OnErrorReceived;
				port.Open();
				_port = port;
				_buffer.Clear();
				IsListening = true;
				Status = "Listening on " + name + ".";
			}
			catch (Exception ex)
			{
				IsListening = false;
				Status = "Scanner port " + name + " is unavailable: " + ex.Message;
			}
		}
	}

	private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e)
	{
		try
		{
			ClosePort();
			IsListening = false;
			Status = "Scanner disconnected. Reconnecting…";
		}
		catch (Exception)
		{
		}
	}

	private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
	{
		try
		{
			SerialPort? port = _port;
			if (port == null || !port.IsOpen)
			{
				return;
			}

			string chunk = port.ReadExisting();
			if (string.IsNullOrEmpty(chunk))
			{
				return;
			}

			string? completed = null;
			lock (_gate)
			{
				_buffer.Append(chunk);
				string text = _buffer.ToString();
				int end = text.IndexOfAny(new[] { '\r', '\n' });
				if (end >= 0)
				{
					completed = text[..end].Trim();
					_buffer.Clear();
					int next = end + 1;
					if (next < text.Length)
					{
						_buffer.Append(text[next..].TrimStart('\r', '\n'));
					}
				}
				else if (_buffer.Length > 128)
				{
					_buffer.Clear();
				}
			}

			if (!string.IsNullOrWhiteSpace(completed) && completed.Length >= 3)
			{
				BarcodeReceived?.Invoke(this, completed);
			}
		}
		catch (Exception)
		{
			IsListening = false;
		}
	}

	private void ClosePort()
	{
		SerialPort? port = _port;
		_port = null;
		if (port == null)
		{
			return;
		}

		try
		{
			port.DataReceived -= OnDataReceived;
			port.ErrorReceived -= OnErrorReceived;
			if (port.IsOpen)
			{
				port.Close();
			}
		}
		catch (Exception)
		{
		}
		finally
		{
			port.Dispose();
		}
	}

	private static string? ReadConfiguredPort()
	{
		try
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "settings", "scanner-com.txt");
			if (!File.Exists(path))
			{
				return null;
			}

			string text = File.ReadAllText(path).Trim();
			return text.Length == 0 ? null : text;
		}
		catch (Exception)
		{
			return null;
		}
	}
}
