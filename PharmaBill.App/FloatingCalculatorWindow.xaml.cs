using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace PharmaBill.App;

public partial class FloatingCalculatorWindow : Window
{
	private decimal _accumulator;
	private string? _pendingOp;
	private bool _resetOnNextDigit = true;

	public FloatingCalculatorWindow()
	{
		InitializeComponent();
	}

	private void Digit_Click(object sender, RoutedEventArgs e)
	{
		string digit = ((Button)sender).Content?.ToString() ?? string.Empty;
		if (_resetOnNextDigit)
		{
			Display.Text = digit == "." ? "0." : digit;
			_resetOnNextDigit = false;
			return;
		}

		if (digit == "." && Display.Text.Contains('.', StringComparison.Ordinal))
		{
			return;
		}

		Display.Text = Display.Text == "0" && digit != "." ? digit : Display.Text + digit;
	}

	private void Op_Click(object sender, RoutedEventArgs e)
	{
		CommitPending();
		_pendingOp = ((Button)sender).Tag?.ToString();
		_resetOnNextDigit = true;
	}

	private void Equals_Click(object sender, RoutedEventArgs e)
	{
		CommitPending();
		_pendingOp = null;
		_resetOnNextDigit = true;
	}

	private void Clear_Click(object sender, RoutedEventArgs e)
	{
		_accumulator = 0m;
		_pendingOp = null;
		Display.Text = "0";
		_resetOnNextDigit = true;
	}

	private void Back_Click(object sender, RoutedEventArgs e)
	{
		if (_resetOnNextDigit || Display.Text.Length <= 1)
		{
			Display.Text = "0";
			_resetOnNextDigit = true;
			return;
		}

		Display.Text = Display.Text[..^1];
	}

	private void Percent_Click(object sender, RoutedEventArgs e)
	{
		if (decimal.TryParse(Display.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value))
		{
			Display.Text = (value / 100m).ToString(CultureInfo.InvariantCulture);
		}
	}

	private void Sign_Click(object sender, RoutedEventArgs e)
	{
		if (decimal.TryParse(Display.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value))
		{
			Display.Text = (-value).ToString(CultureInfo.InvariantCulture);
		}
	}

	private void CommitPending()
	{
		if (!decimal.TryParse(Display.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal current))
		{
			return;
		}

		if (_pendingOp is null)
		{
			_accumulator = current;
			return;
		}

		_accumulator = _pendingOp switch
		{
			"+" => _accumulator + current,
			"-" => _accumulator - current,
			"*" => _accumulator * current,
			"/" => current == 0m ? _accumulator : _accumulator / current,
			_ => current
		};
		Display.Text = _accumulator.ToString("0.####", CultureInfo.InvariantCulture);
	}
}
