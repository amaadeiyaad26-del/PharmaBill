using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;

namespace PharmaBill.App.Controls;

public partial class InfoHelper : UserControl, IComponentConnector
{
	public static readonly DependencyProperty TitleProperty = DependencyProperty.Register("Title", typeof(string), typeof(InfoHelper), new PropertyMetadata(string.Empty));

	public static readonly DependencyProperty BodyProperty = DependencyProperty.Register("Body", typeof(string), typeof(InfoHelper), new PropertyMetadata(string.Empty));

	public static readonly DependencyProperty OpenOnHoverProperty = DependencyProperty.Register("OpenOnHover", typeof(bool), typeof(InfoHelper), new PropertyMetadata(true));

	private DispatcherTimer? _hoverOpenTimer;

	public string Title
	{
		get
		{
			return (string)GetValue(TitleProperty);
		}
		set
		{
			SetValue(TitleProperty, value);
		}
	}

	public string Body
	{
		get
		{
			return (string)GetValue(BodyProperty);
		}
		set
		{
			SetValue(BodyProperty, value);
		}
	}

	public bool OpenOnHover
	{
		get
		{
			return (bool)GetValue(OpenOnHoverProperty);
		}
		set
		{
			SetValue(OpenOnHoverProperty, value);
		}
	}

	public InfoHelper()
	{
		InitializeComponent();
	}

	private void InfoButton_OnClick(object sender, RoutedEventArgs e)
	{
		CancelHoverOpen();
		HelpPopup.IsOpen = !HelpPopup.IsOpen;
		e.Handled = true;
	}

	private void InfoButton_OnMouseEnter(object sender, MouseEventArgs e)
	{
		if (!OpenOnHover || HelpPopup.IsOpen)
		{
			return;
		}
		CancelHoverOpen();
		_hoverOpenTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(350L)
		};
		_hoverOpenTimer.Tick += (object? _, EventArgs _) =>
		{
			CancelHoverOpen();
			if (IsMouseOver || InfoButton.IsMouseOver)
			{
				HelpPopup.IsOpen = true;
			}
		};
		_hoverOpenTimer.Start();
	}

	private void CloseButton_OnClick(object sender, RoutedEventArgs e)
	{
		HelpPopup.IsOpen = false;
		e.Handled = true;
	}

	private void HelpPopup_OnClosed(object? sender, EventArgs e)
	{
		CancelHoverOpen();
	}

	private void CancelHoverOpen()
	{
		if (_hoverOpenTimer != null)
		{
			_hoverOpenTimer.Stop();
			_hoverOpenTimer = null;
		}
	}
}
