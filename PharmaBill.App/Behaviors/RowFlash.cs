using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PharmaBill.App.Behaviors;

public static class RowFlash
{
	private sealed class FlashState
	{
		public HashSet<object> Pending { get; } = new HashSet<object>(ReferenceEqualityComparer.Instance);

		public INotifyCollectionChanged? Source { get; set; }

		public NotifyCollectionChangedEventHandler? Handler { get; set; }
	}

	private static readonly Color FlashColor = (Color)ColorConverter.ConvertFromString("#FFECFDF5");

	private static readonly Duration FlashDuration = new Duration(TimeSpan.FromMilliseconds(400L));

	public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(RowFlash), new PropertyMetadata(false, OnEnabledChanged));

	private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(FlashState), typeof(RowFlash));

	public static bool GetEnabled(DependencyObject element)
	{
		return (bool)element.GetValue(EnabledProperty);
	}

	public static void SetEnabled(DependencyObject element, bool value)
	{
		element.SetValue(EnabledProperty, value);
	}

	private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is DataGrid dataGrid && (bool)e.NewValue != (bool)e.OldValue)
		{
			if ((bool)e.NewValue)
			{
				dataGrid.SetValue(StateProperty, new FlashState());
				dataGrid.LoadingRow += OnLoadingRow;
				dataGrid.Loaded += OnGridLoaded;
				dataGrid.Unloaded += OnGridUnloaded;
				if (dataGrid.IsLoaded)
				{
					Attach(dataGrid);
				}
			}
			else
			{
				dataGrid.LoadingRow -= OnLoadingRow;
				dataGrid.Loaded -= OnGridLoaded;
				dataGrid.Unloaded -= OnGridUnloaded;
				Detach(dataGrid);
				dataGrid.ClearValue(StateProperty);
			}
		}
	}

	// The bill grids are rebuilt on every visit to a billing screen while their bills live all session. Watching only
	// while on screen keeps a closed screen's grid from being held by the bill (and by the property descriptor's
	// static table, which holds every grid registered with it).
	private static readonly DependencyPropertyDescriptor ItemsSourceDescriptor = DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid));

	private static void OnGridLoaded(object sender, RoutedEventArgs e) => Attach((DataGrid)sender);

	private static void OnGridUnloaded(object sender, RoutedEventArgs e) => Detach((DataGrid)sender);

	private static void Attach(DataGrid grid)
	{
		ItemsSourceDescriptor.RemoveValueChanged(grid, OnItemsSourceChanged);
		ItemsSourceDescriptor.AddValueChanged(grid, OnItemsSourceChanged);
		Hook(grid);
	}

	private static void Detach(DataGrid grid)
	{
		ItemsSourceDescriptor.RemoveValueChanged(grid, OnItemsSourceChanged);
		Unhook(grid);
	}

	private static void OnItemsSourceChanged(object? sender, EventArgs e)
	{
		if (sender is DataGrid grid)
		{
			Hook(grid);
		}
	}

	private static void Hook(DataGrid grid)
	{
		object value = grid.GetValue(StateProperty);
		FlashState? state = value as FlashState;
		if (state == null)
		{
			return;
		}
		Unhook(grid);
		if (!(grid.ItemsSource is INotifyCollectionChanged notifyCollectionChanged))
		{
			return;
		}
		state.Source = notifyCollectionChanged;
		state.Handler = (object? _, NotifyCollectionChangedEventArgs args) =>
		{
			if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems != null)
			{
				foreach (object newItem in args.NewItems)
				{
					state.Pending.Add(newItem);
				}
				return;
			}
			if (args.Action == NotifyCollectionChangedAction.Reset)
			{
				state.Pending.Clear();
			}
		};
		notifyCollectionChanged.CollectionChanged += state.Handler;
	}

	private static void Unhook(DataGrid grid)
	{
		if (grid.GetValue(StateProperty) is FlashState { Source: not null, Handler: not null } flashState)
		{
			flashState.Source.CollectionChanged -= flashState.Handler;
			flashState.Source = null;
			flashState.Handler = null;
			flashState.Pending.Clear();
		}
	}

	private static void OnLoadingRow(object? sender, DataGridRowEventArgs e)
	{
		if (sender is DataGrid dataGrid && dataGrid.GetValue(StateProperty) is FlashState flashState && flashState.Pending.Remove(e.Row.Item) && SystemParameters.ClientAreaAnimation)
		{
			DataGridRow row = e.Row;
			Color toValue = (row.Background as SolidColorBrush)?.Color ?? Colors.Transparent;
			SolidColorBrush solidColorBrush = new SolidColorBrush(FlashColor);
			row.Background = solidColorBrush;
			ColorAnimation colorAnimation = new ColorAnimation(FlashColor, toValue, FlashDuration)
			{
				EasingFunction = new QuadraticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			colorAnimation.Completed += (object? _, EventArgs _) =>
			{
				row.ClearValue(Control.BackgroundProperty);
			};
			solidColorBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);
		}
	}
}
