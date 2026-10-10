using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PharmaBill.App.Controls;

namespace PharmaBill.App.Behaviors;

public static class CyclingPlaceholder
{
	private sealed class PlaceholderState : IDisposable
	{
		private readonly TextBox _box;

		private readonly DispatcherTimer _timer;

		private PlaceholderAdorner? _adorner;

		private bool _disposed;

		public string[] Hints { get; set; }

		public int Index { get; set; }

		public PlaceholderState(TextBox box, string[] hints, double intervalSeconds)
		{
			_box = box;
			Hints = hints;
			_timer = new DispatcherTimer(DispatcherPriority.Background)
			{
				Interval = TimeSpan.FromSeconds((intervalSeconds > 0.5) ? intervalSeconds : 2.5)
			};
			_timer.Tick += OnTick;
		}

		public void OnLoaded(object sender, RoutedEventArgs e)
		{
			_box.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RefreshVisibility));
		}

		public void OnUnloaded(object sender, RoutedEventArgs e)
		{
			Stop();
		}

		public void OnFocusChanged(object sender, RoutedEventArgs e)
		{
			if (!_disposed && _box.IsLoaded && PresentationSource.FromVisual(_box) != null)
			{
				RefreshVisibility();
			}
		}

		public void OnTextChanged(object sender, TextChangedEventArgs e)
		{
			RefreshVisibility();
		}

		public void RefreshVisibility()
		{
			if (_disposed)
			{
				return;
			}
			EnsureAdorner();
			if (_adorner == null)
			{
				return;
			}
			bool show = string.IsNullOrEmpty(_box.Text)
				&& !_box.IsKeyboardFocusWithin
				&& !_box.IsFocused
				&& !_box.IsKeyboardFocused;
			_adorner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
			_adorner.Opacity = show ? 1.0 : 0.0;
			if (show)
			{
				if (!_timer.IsEnabled)
				{
					_adorner.SetText(CurrentHint(), animate: false);
					_timer.Start();
				}
			}
			else
			{
				_timer.Stop();
			}
		}

		private void OnTick(object? sender, EventArgs e)
		{
			if (_adorner != null && Hints.Length != 0)
			{
				Index = (Index + 1) % Hints.Length;
				_adorner.SetText(CurrentHint(), animate: true);
			}
		}

		private string CurrentHint()
		{
			if (Hints.Length != 0)
			{
				return Hints[Math.Clamp(Index, 0, Hints.Length - 1)];
			}
			return string.Empty;
		}

		private void EnsureAdorner()
		{
			if (_adorner == null && !_disposed && _box.IsLoaded && _box.IsVisible && PresentationSource.FromVisual(_box) != null && !(_box.ActualWidth <= 0.0) && !(_box.ActualHeight <= 0.0))
			{
				AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(_box);
				if (adornerLayer != null)
				{
					_adorner = new PlaceholderAdorner(_box);
					_adorner.SetText(CurrentHint(), animate: false);
					adornerLayer.Add(_adorner);
				}
			}
		}

		private void Stop()
		{
			_timer.Stop();
			if (_adorner != null)
			{
				AdornerLayer.GetAdornerLayer(_box)?.Remove(_adorner);
				_adorner.DisposeVisuals();
				_adorner = null;
			}
		}

		public void Dispose()
		{
			if (!_disposed)
			{
				_disposed = true;
				_timer.Tick -= OnTick;
				Stop();
			}
		}
	}

	private sealed class PlaceholderAdorner : Adorner
	{
		private TextBlock? _label;

		private TranslateTransform? _slide;

		private VisualCollection? _visuals;

		protected override int VisualChildrenCount
		{
			get
			{
				if (_visuals == null)
				{
					return (_label != null) ? 1 : 0;
				}
				return _visuals.Count;
			}
		}

		public PlaceholderAdorner(UIElement adornedElement)
			: base(adornedElement)
		{
			IsHitTestVisible = false;
			_slide = new TranslateTransform();
			_label = new TextBlock
			{
				Foreground = new SolidColorBrush(Color.FromArgb(153, 100, 116, 139)),
				FontSize = 14.0,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(10.0, 0.0, 10.0, 0.0),
				TextTrimming = TextTrimming.CharacterEllipsis,
				RenderTransform = _slide
			};
			_visuals = new VisualCollection(this) { _label };
		}

		public void SetText(string text, bool animate)
		{
			if (_label == null || _slide == null)
			{
				return;
			}
			if (!animate)
			{
				_label.BeginAnimation(UIElement.OpacityProperty, null);
				_slide.BeginAnimation(TranslateTransform.YProperty, null);
				_label.Opacity = 1.0;
				_slide.Y = 0.0;
				_label.Text = text;
				return;
			}
			DoubleAnimation doubleAnimation = new DoubleAnimation(1.0, 0.0, TransitionDuration)
			{
				EasingFunction = new QuadraticEase
				{
					EasingMode = EasingMode.EaseIn
				}
			};
			DoubleAnimation animation = new DoubleAnimation(0.0, -10.0, TransitionDuration)
			{
				EasingFunction = new QuadraticEase
				{
					EasingMode = EasingMode.EaseIn
				}
			};
			doubleAnimation.Completed += (object? _, EventArgs _) =>
			{
				if (_label != null && _slide != null)
				{
					_label.Text = text;
					_slide.Y = 12.0;
					DoubleAnimation animation2 = new DoubleAnimation(0.0, 1.0, TransitionDuration)
					{
						EasingFunction = new QuadraticEase
						{
							EasingMode = EasingMode.EaseOut
						}
					};
					DoubleAnimation animation3 = new DoubleAnimation(12.0, 0.0, TransitionDuration)
					{
						EasingFunction = new QuadraticEase
						{
							EasingMode = EasingMode.EaseOut
						}
					};
					_label.BeginAnimation(UIElement.OpacityProperty, animation2);
					_slide.BeginAnimation(TranslateTransform.YProperty, animation3);
				}
			};
			_label.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
			_slide.BeginAnimation(TranslateTransform.YProperty, animation);
		}

		public void DisposeVisuals()
		{
			_visuals?.Clear();
			_visuals = null;
			_label = null;
			_slide = null;
		}

		protected override Visual GetVisualChild(int index)
		{
			if (_visuals != null)
			{
				if (index < 0 || index >= _visuals.Count)
				{
					throw new ArgumentOutOfRangeException("index");
				}
				return _visuals[index] ?? throw new ArgumentOutOfRangeException("index", "Visual child is null.");
			}
			if (_label != null && index == 0)
			{
				return _label;
			}
			throw new ArgumentOutOfRangeException("index");
		}

		protected override Size MeasureOverride(Size constraint)
		{
			if (_label == null)
			{
				return new Size(0.0, 0.0);
			}
			_label.Measure(constraint);
			return AdornedElement?.RenderSize ?? new Size(0.0, 0.0);
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			_label?.Arrange(new Rect(finalSize));
			return finalSize;
		}
	}

	private sealed class FloatingLabelState : IDisposable
	{
		private readonly FloatingField _field;

		private readonly DispatcherTimer _timer;

		private readonly string _restLabel;

		private bool _disposed;

		public string[] Hints { get; set; }

		public int Index { get; set; }

		public RoutedEventHandler TextChangedHandler { get; }

		public FloatingLabelState(FloatingField field, string[] hints, double intervalSeconds)
		{
			_field = field;
			_restLabel = field.Label;
			Hints = hints;
			TextChangedHandler = (object _, RoutedEventArgs _) =>
			{
				Refresh();
			};
			_timer = new DispatcherTimer(DispatcherPriority.Background)
			{
				Interval = TimeSpan.FromSeconds((intervalSeconds > 0.5) ? intervalSeconds : 2.5)
			};
			_timer.Tick += OnTick;
		}

		public void OnLoaded(object sender, RoutedEventArgs e)
		{
			Refresh();
		}

		public void OnUnloaded(object sender, RoutedEventArgs e)
		{
			_timer.Stop();
		}

		public void OnFocusChanged(object sender, DependencyPropertyChangedEventArgs e)
		{
			Refresh();
		}

		public void Refresh()
		{
			if (_disposed || !_field.IsLoaded || PresentationSource.FromVisual(_field) == null)
			{
				return;
			}
			if (!_field.IsKeyboardFocusWithin && !_field.HasValue)
			{
				if (!_timer.IsEnabled)
				{
					_field.Label = CurrentHint();
					_timer.Start();
				}
			}
			else
			{
				_timer.Stop();
				_field.Label = _restLabel;
			}
		}

		private void OnTick(object? sender, EventArgs e)
		{
			if (!_disposed && Hints.Length != 0 && _field.IsLoaded)
			{
				Index = (Index + 1) % Hints.Length;
				_field.Label = CurrentHint();
			}
		}

		private string CurrentHint()
		{
			if (Hints.Length != 0)
			{
				return Hints[Math.Clamp(Index, 0, Hints.Length - 1)];
			}
			return _restLabel;
		}

		public void Dispose()
		{
			if (!_disposed)
			{
				_disposed = true;
				_timer.Tick -= OnTick;
				_timer.Stop();
				_field.Label = _restLabel;
			}
		}
	}

	private static readonly Duration TransitionDuration = new Duration(TimeSpan.FromMilliseconds(280L));

	private static readonly string[] DefaultHints = new string[3] { "Search 'Paracetamol 650mg'...", "Search 'Amoxicillin 500mg'...", "Search 'Azithromycin'..." };

	public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(CyclingPlaceholder), new PropertyMetadata(false, OnEnabledChanged));

	public static readonly DependencyProperty HintsProperty = DependencyProperty.RegisterAttached("Hints", typeof(string), typeof(CyclingPlaceholder), new PropertyMetadata(null, OnHintsChanged));

	public static readonly DependencyProperty IntervalSecondsProperty = DependencyProperty.RegisterAttached("IntervalSeconds", typeof(double), typeof(CyclingPlaceholder), new PropertyMetadata(2.5));

	private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(object), typeof(CyclingPlaceholder));

	public static bool GetEnabled(DependencyObject element)
	{
		return (bool)element.GetValue(EnabledProperty);
	}

	public static void SetEnabled(DependencyObject element, bool value)
	{
		element.SetValue(EnabledProperty, value);
	}

	public static string? GetHints(DependencyObject element)
	{
		return (string)element.GetValue(HintsProperty);
	}

	public static void SetHints(DependencyObject element, string? value)
	{
		element.SetValue(HintsProperty, value);
	}

	public static double GetIntervalSeconds(DependencyObject element)
	{
		return (double)element.GetValue(IntervalSecondsProperty);
	}

	public static void SetIntervalSeconds(DependencyObject element, double value)
	{
		element.SetValue(IntervalSecondsProperty, value);
	}

	private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if ((bool)e.NewValue)
		{
			if (d is TextBox box)
			{
				Attach(box);
			}
			else if (d is FloatingField field)
			{
				AttachField(field);
			}
		}
		else if (d is TextBox box2)
		{
			Detach(box2);
		}
		else if (d is FloatingField field2)
		{
			DetachField(field2);
		}
	}

	private static void OnHintsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d.GetValue(StateProperty) is PlaceholderState placeholderState)
		{
			placeholderState.Hints = ParseHints(e.NewValue as string);
			placeholderState.Index = 0;
			placeholderState.RefreshVisibility();
		}
		else if (d.GetValue(StateProperty) is FloatingLabelState floatingLabelState)
		{
			floatingLabelState.Hints = ParseHints(e.NewValue as string);
			floatingLabelState.Index = 0;
			floatingLabelState.Refresh();
		}
	}

	private static void Attach(TextBox box)
	{
		if (!(box.GetValue(StateProperty) is PlaceholderState))
		{
			PlaceholderState placeholderState = new PlaceholderState(box, ParseHints(GetHints(box)), GetIntervalSeconds(box));
			box.SetValue(StateProperty, placeholderState);
			box.Loaded += placeholderState.OnLoaded;
			box.Unloaded += placeholderState.OnUnloaded;
			box.GotKeyboardFocus += placeholderState.OnFocusChanged;
			box.LostKeyboardFocus += placeholderState.OnFocusChanged;
			box.TextChanged += placeholderState.OnTextChanged;
			if (box.IsLoaded)
			{
				placeholderState.OnLoaded(box, new RoutedEventArgs());
			}
		}
	}

	private static void Detach(TextBox box)
	{
		if (box.GetValue(StateProperty) is PlaceholderState placeholderState)
		{
			box.Loaded -= placeholderState.OnLoaded;
			box.Unloaded -= placeholderState.OnUnloaded;
			box.GotKeyboardFocus -= placeholderState.OnFocusChanged;
			box.LostKeyboardFocus -= placeholderState.OnFocusChanged;
			box.TextChanged -= placeholderState.OnTextChanged;
			placeholderState.Dispose();
			box.ClearValue(StateProperty);
		}
	}

	private static void AttachField(FloatingField field)
	{
		if (!(field.GetValue(StateProperty) is FloatingLabelState))
		{
			FloatingLabelState floatingLabelState = new FloatingLabelState(field, ParseHints(GetHints(field)), GetIntervalSeconds(field));
			field.SetValue(StateProperty, floatingLabelState);
			field.Loaded += floatingLabelState.OnLoaded;
			field.Unloaded += floatingLabelState.OnUnloaded;
			field.IsKeyboardFocusWithinChanged += floatingLabelState.OnFocusChanged;
			field.AddHandler(TextBoxBase.TextChangedEvent, floatingLabelState.TextChangedHandler);
			if (field.IsLoaded)
			{
				floatingLabelState.OnLoaded(field, new RoutedEventArgs());
			}
		}
	}

	private static void DetachField(FloatingField field)
	{
		if (field.GetValue(StateProperty) is FloatingLabelState floatingLabelState)
		{
			field.Loaded -= floatingLabelState.OnLoaded;
			field.Unloaded -= floatingLabelState.OnUnloaded;
			field.IsKeyboardFocusWithinChanged -= floatingLabelState.OnFocusChanged;
			field.RemoveHandler(TextBoxBase.TextChangedEvent, floatingLabelState.TextChangedHandler);
			floatingLabelState.Dispose();
			field.ClearValue(StateProperty);
		}
	}

	private static string[] ParseHints(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return DefaultHints;
		}
		string[] array = raw.Split(new char[3] { '|', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (array.Length != 0)
		{
			return array;
		}
		return DefaultHints;
	}
}
