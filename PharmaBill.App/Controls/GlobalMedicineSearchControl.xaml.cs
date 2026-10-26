using System.Windows;
using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PharmaBill.App.ViewModels;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PharmaBill.App.Controls;

public partial class GlobalMedicineSearchControl : UserControl
{
	private readonly string[] _searchHints = new string[5] { "Search 'Paracetamol 650mg'...", "Search 'Amoxicillin 500mg'...", "Search 'Azithromycin'...", "Search 'Pantoprazole'...", "Search 'Cetirizine 10mg'..." };

	public static readonly DependencyProperty ShowActionsProperty = DependencyProperty.Register("ShowActions", typeof(bool), typeof(GlobalMedicineSearchControl), new PropertyMetadata(false));

	private int _hintIndex;

	private DispatcherTimer? _hintTimer;

	public bool ShowActions
	{
		get
		{
			return (bool)GetValue(ShowActionsProperty);
		}
		set
		{
			SetValue(ShowActionsProperty, value);
		}
	}

	private CatalogSearchViewModel? _search;

	public GlobalMedicineSearchControl()
	{
		InitializeComponent();
		// The search view model lives for the whole session and this control is built on every visit to the page:
		// it watches the results only while it is on screen, so a closed page is not kept alive by the view model.
		Loaded += (object _, RoutedEventArgs _) =>
		{
			InitAnimatedSearchHint();
			Watch(DataContext as CatalogSearchViewModel);
		};
		Unloaded += (object _, RoutedEventArgs _) =>
		{
			_hintTimer?.Stop();
			_hintTimer = null;
			ResultsFlyout.IsOpen = false;
			Watch(null);
		};
		DataContextChanged += (_, _) =>
		{
			if (IsLoaded)
			{
				Watch(DataContext as CatalogSearchViewModel);
			}
		};
		IsVisibleChanged += (_, _) =>
		{
			if (!IsVisible)
			{
				ResultsFlyout.IsOpen = false;
			}
		};
	}

	/// <summary>True while the results flyout is showing (for tests and keyboard checks).</summary>
	public bool IsResultsFlyoutOpen => ResultsFlyout.IsOpen;

	private bool HasResults => _search is { } search && (search.HasInStock || search.HasCatalog || search.HasSubstitutes);

	// The header search shares this view model, so the flyout opens only for searches made from this control:
	// while its own search row has the keyboard, or while the flyout is already showing (e.g. Substitutes).
	private void Watch(CatalogSearchViewModel? search)
	{
		if (_search != null)
		{
			_search.InStockResults.CollectionChanged -= OnResultsChanged;
			_search.CatalogResults.CollectionChanged -= OnResultsChanged;
			_search.SubstituteResults.CollectionChanged -= OnResultsChanged;
		}
		_search = search;
		if (search != null)
		{
			search.InStockResults.CollectionChanged += OnResultsChanged;
			search.CatalogResults.CollectionChanged += OnResultsChanged;
			search.SubstituteResults.CollectionChanged += OnResultsChanged;
		}
	}

	private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (!HasResults)
		{
			ResultsFlyout.IsOpen = false;
		}
		else if (IsVisible && (ResultsFlyout.IsOpen || SearchRow.IsKeyboardFocusWithin))
		{
			ResultsFlyout.IsOpen = true;
		}
	}

	private void SearchRow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		// Back in the search box after the flyout was closed: show the results again.
		// Its buttons (Search, + New Medicine) do their own thing.
		if (HasResults && e.OriginalSource is DependencyObject source && !IsInsideButton(source))
		{
			ResultsFlyout.IsOpen = true;
		}
	}

	private bool IsInsideButton(DependencyObject source)
	{
		for (DependencyObject? current = source; current is Visual && current != SearchRow; current = VisualTreeHelper.GetParent(current))
		{
			if (current is ButtonBase)
			{
				return true;
			}
		}
		return false;
	}

	private void InitAnimatedSearchHint()
	{
		if (_hintTimer != null)
		{
			return;
		}
		TxtSearchHint.Text = _searchHints[0];
		_hintTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(2.5)
		};
		_hintTimer.Tick += (object? _, EventArgs _) =>
		{
			if (!string.IsNullOrEmpty(SearchBox.Text) || SearchBox.IsKeyboardFocusWithin)
			{
				TxtSearchHint.Visibility = Visibility.Collapsed;
			}
			else
			{
				TxtSearchHint.Visibility = Visibility.Visible;
				_hintIndex = (_hintIndex + 1) % _searchHints.Length;
				DoubleAnimation animation = new DoubleAnimation(0.0, -10.0, TimeSpan.FromMilliseconds(180L));
				DoubleAnimation doubleAnimation = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(180L));
				doubleAnimation.Completed += (object? obj, EventArgs e) =>
				{
					TxtSearchHint.Text = _searchHints[_hintIndex];
					DoubleAnimation animation2 = new DoubleAnimation(10.0, 0.0, TimeSpan.FromMilliseconds(180L));
					DoubleAnimation animation3 = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180L));
					TxtSearchHint.RenderTransform.BeginAnimation(TranslateTransform.YProperty, animation2);
					TxtSearchHint.BeginAnimation(UIElement.OpacityProperty, animation3);
				};
				TxtSearchHint.RenderTransform.BeginAnimation(TranslateTransform.YProperty, animation);
				TxtSearchHint.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
			}
		};
		_hintTimer.Start();
	}

	private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		TxtSearchHint.Visibility = ((!string.IsNullOrEmpty(SearchBox.Text)) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void SearchBox_FocusChanged(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (SearchBox.IsKeyboardFocusWithin || !string.IsNullOrEmpty(SearchBox.Text))
		{
			TxtSearchHint.Visibility = Visibility.Collapsed;
		}
		else
		{
			TxtSearchHint.Visibility = Visibility.Visible;
		}
	}

}
