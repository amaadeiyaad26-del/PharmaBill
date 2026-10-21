using System.Windows;
using System.Windows.Controls;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App.Controls;

/// <summary>
/// Dashboard heading: the shop's name as a wordmark, its address and phone beneath (left out when empty), its logo
/// beside it when one is uploaded, and a small page label such as "Dashboard".
/// </summary>
public partial class ShopIdentityBlock : UserControl
{
	public static readonly DependencyProperty ShopProperty = DependencyProperty.Register(
		nameof(Shop), typeof(ShopIdentityViewModel), typeof(ShopIdentityBlock), new PropertyMetadata(null));

	public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
		nameof(Label), typeof(string), typeof(ShopIdentityBlock), new PropertyMetadata("Dashboard"));

	public static readonly DependencyProperty NoteProperty = DependencyProperty.Register(
		nameof(Note), typeof(string), typeof(ShopIdentityBlock), new PropertyMetadata(string.Empty, OnNoteChanged));

	private static readonly DependencyPropertyKey NoteSeparatorKey = DependencyProperty.RegisterReadOnly(
		nameof(NoteSeparator), typeof(string), typeof(ShopIdentityBlock), new PropertyMetadata(string.Empty));

	public ShopIdentityViewModel? Shop
	{
		get => (ShopIdentityViewModel?)GetValue(ShopProperty);
		set => SetValue(ShopProperty, value);
	}

	public string Label
	{
		get => (string)GetValue(LabelProperty);
		set => SetValue(LabelProperty, value);
	}

	/// <summary>Optional quiet text after the label, such as "Updated 21-Oct-2026 15:30".</summary>
	public string Note
	{
		get => (string)GetValue(NoteProperty);
		set => SetValue(NoteProperty, value);
	}

	public string NoteSeparator => (string)GetValue(NoteSeparatorKey.DependencyProperty);

	public ShopIdentityBlock()
	{
		InitializeComponent();
	}

	private static void OnNoteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
		d.SetValue(NoteSeparatorKey, string.IsNullOrWhiteSpace(e.NewValue as string) ? string.Empty : "  ·  ");
}
