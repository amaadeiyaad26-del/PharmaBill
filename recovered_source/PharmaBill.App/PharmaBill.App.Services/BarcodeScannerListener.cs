using System;
using System.Diagnostics;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;

namespace PharmaBill.App.Services;

public sealed class BarcodeScannerListener
{
	private readonly StringBuilder _buffer = new StringBuilder();

	private readonly Stopwatch _stopwatch = new Stopwatch();

	private bool _sawHardwareVelocity;

	public const int MaxInterKeyIntervalMs = 50;

	public const int MinBarcodeLength = 3;

	public event Action<string>? BarcodeScanned;

	public void ProcessKeyDown(KeyEventArgs e)
	{
		if (ProcessKey(e.Key))
		{
			e.Handled = true;
		}
	}

	public bool ProcessKey(Key key)
	{
		bool flag;
		switch (key)
		{
		case Key.Return:
			if (_buffer.Length >= 3 && _sawHardwareVelocity)
			{
				string text = _buffer.ToString().Trim();
				Reset();
				if (text.Length >= 3)
				{
					StripInjectedSuffixFromFocusedEditor(text);
					BarcodeScanned?.Invoke(text);
					return true;
				}
			}
			else
			{
				Reset();
			}
			return false;
		case Key.Back:
		case Key.Tab:
		case Key.Escape:
		case Key.Delete:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			Reset();
			return false;
		}
		if (_stopwatch.IsRunning)
		{
			if (_stopwatch.ElapsedMilliseconds > 50)
			{
				_buffer.Clear();
				_sawHardwareVelocity = false;
			}
			else if (_buffer.Length > 0)
			{
				_sawHardwareVelocity = true;
			}
		}
		_stopwatch.Restart();
		return false;
	}

	public void ProcessTextInput(TextCompositionEventArgs e)
	{
		if (!string.IsNullOrEmpty(e.Text) && AppendText(e.Text) && _sawHardwareVelocity)
		{
			e.Handled = true;
		}
	}

	public bool AppendText(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		_buffer.Append(text);
		return true;
	}

	public void SimulateScan(string barcode)
	{
		string text = barcode?.Trim() ?? string.Empty;
		if (text.Length >= 3)
		{
			Reset();
			BarcodeScanned?.Invoke(text);
		}
	}

	public void Reset()
	{
		_buffer.Clear();
		_stopwatch.Reset();
		_sawHardwareVelocity = false;
	}

	private static void StripInjectedSuffixFromFocusedEditor(string barcode)
	{
		try
		{
			if (!(Keyboard.FocusedElement is TextBox { IsReadOnly: false } textBox))
			{
				return;
			}
			string text = textBox.Text ?? string.Empty;
			if (text.EndsWith(barcode, StringComparison.Ordinal))
			{
				string text2 = text;
				int length = barcode.Length;
				textBox.Text = text2.Substring(0, text2.Length - length);
				textBox.CaretIndex = textBox.Text.Length;
			}
			else if (barcode.Length > 0 && text.Length > 0)
			{
				if (text[text.Length - 1] == barcode[0])
				{
					string text2 = text;
					textBox.Text = text2.Substring(0, text2.Length - 1);
					textBox.CaretIndex = textBox.Text.Length;
				}
			}
		}
		catch (InvalidOperationException)
		{
		}
	}
}
