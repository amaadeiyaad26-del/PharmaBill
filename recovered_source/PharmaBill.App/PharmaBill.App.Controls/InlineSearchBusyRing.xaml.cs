using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace PharmaBill.App.Controls;

public partial class InlineSearchBusyRing : UserControl, IComponentConnector
{
	public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register("IsActive", typeof(bool), typeof(InlineSearchBusyRing), new PropertyMetadata(false, OnIsActiveChanged));

	public bool IsActive
	{
		get
		{
			return (bool)GetValue(IsActiveProperty);
		}
		set
		{
			SetValue(IsActiveProperty, value);
		}
	}

	public InlineSearchBusyRing()
	{
		InitializeComponent();
	}

	private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is InlineSearchBusyRing inlineSearchBusyRing)
		{
			inlineSearchBusyRing.Visibility = ((!(bool)e.NewValue) ? Visibility.Collapsed : Visibility.Visible);
		}
	}
}
