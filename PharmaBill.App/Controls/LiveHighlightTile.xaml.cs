using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PharmaBill.App.Controls;

public partial class LiveHighlightTile : UserControl
{
	public static readonly DependencyProperty Headline1Property = DependencyProperty.Register("Headline1", typeof(string), typeof(LiveHighlightTile), new PropertyMetadata(string.Empty, OnSlidesChanged));

	public static readonly DependencyProperty Sub1Property = DependencyProperty.Register("Sub1", typeof(string), typeof(LiveHighlightTile), new PropertyMetadata(string.Empty, OnSlidesChanged));

	public static readonly DependencyProperty Headline2Property = DependencyProperty.Register("Headline2", typeof(string), typeof(LiveHighlightTile), new PropertyMetadata(string.Empty, OnSlidesChanged));

	public static readonly DependencyProperty Sub2Property = DependencyProperty.Register("Sub2", typeof(string), typeof(LiveHighlightTile), new PropertyMetadata(string.Empty, OnSlidesChanged));

	public static readonly DependencyProperty Headline3Property = DependencyProperty.Register("Headline3", typeof(string), typeof(LiveHighlightTile), new PropertyMetadata(string.Empty, OnSlidesChanged));

	public static readonly DependencyProperty Sub3Property = DependencyProperty.Register("Sub3", typeof(string), typeof(LiveHighlightTile), new PropertyMetadata(string.Empty, OnSlidesChanged));

	private readonly List<(string Title, string Sub)> _infoSlides;

	private int _currentSlide;

	private DispatcherTimer? _slideTimer;

	private bool _ready;

	public string Headline1
	{
		get
		{
			return (string)GetValue(Headline1Property);
		}
		set
		{
			SetValue(Headline1Property, value);
		}
	}

	public string Sub1
	{
		get
		{
			return (string)GetValue(Sub1Property);
		}
		set
		{
			SetValue(Sub1Property, value);
		}
	}

	public string Headline2
	{
		get
		{
			return (string)GetValue(Headline2Property);
		}
		set
		{
			SetValue(Headline2Property, value);
		}
	}

	public string Sub2
	{
		get
		{
			return (string)GetValue(Sub2Property);
		}
		set
		{
			SetValue(Sub2Property, value);
		}
	}

	public string Headline3
	{
		get
		{
			return (string)GetValue(Headline3Property);
		}
		set
		{
			SetValue(Headline3Property, value);
		}
	}

	public string Sub3
	{
		get
		{
			return (string)GetValue(Sub3Property);
		}
		set
		{
			SetValue(Sub3Property, value);
		}
	}

	public LiveHighlightTile()
	{
		int num = 3;
		List<(string, string)> list = new List<(string, string)>(num);
		CollectionsMarshal.SetCount(list, num);
		Span<(string, string)> span = CollectionsMarshal.AsSpan(list);
		span[0] = ("Today: —", "No invoices yet • System healthy");
		span[1] = ("Stock Alert", "No batches nearing expiry");
		span[2] = ("Mobile LAN Sync", "Host status unavailable");
		_infoSlides = list;
		InitializeComponent();
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
		IsVisibleChanged += OnIsVisibleChanged;
	}

	private static void OnSlidesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is LiveHighlightTile liveHighlightTile)
		{
			liveHighlightTile.SyncSlidesFromBindings();
			if (liveHighlightTile._ready)
			{
				liveHighlightTile.ApplyCurrentSlide(animate: false);
			}
		}
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		_ready = true;
		SyncSlidesFromBindings();
		ApplyCurrentSlide(animate: false);
		InitInfoCarousel();
	}

	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		_ready = false;
		if (_slideTimer != null)
		{
			_slideTimer.Stop();
			_slideTimer.Tick -= OnSlideTick;
			_slideTimer = null;
		}
	}

	private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
		if (_slideTimer != null)
		{
			if (IsVisible && IsLoaded)
			{
				_slideTimer.Start();
			}
			else
			{
				_slideTimer.Stop();
			}
		}
	}

	private void SyncSlidesFromBindings()
	{
		_infoSlides[0] = (string.IsNullOrWhiteSpace(Headline1) ? "Today: —" : Headline1, string.IsNullOrWhiteSpace(Sub1) ? "No invoices yet • System healthy" : Sub1);
		_infoSlides[1] = (string.IsNullOrWhiteSpace(Headline2) ? "Stock Alert" : Headline2, string.IsNullOrWhiteSpace(Sub2) ? "No batches nearing expiry" : Sub2);
		_infoSlides[2] = (string.IsNullOrWhiteSpace(Headline3) ? "Mobile LAN Sync" : Headline3, string.IsNullOrWhiteSpace(Sub3) ? "Host status unavailable" : Sub3);
	}

	private void InitInfoCarousel()
	{
		if (_slideTimer == null)
		{
			_slideTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(4L)
			};
			_slideTimer.Tick += OnSlideTick;
			if (IsVisible)
			{
				_slideTimer.Start();
			}
		}
	}

	private void OnSlideTick(object? sender, EventArgs e)
	{
		_currentSlide = (_currentSlide + 1) % _infoSlides.Count;
		ApplyCurrentSlide(animate: true);
	}

	private void ApplyCurrentSlide(bool animate)
	{
		(string Title, string Sub) tuple = _infoSlides[_currentSlide];
		string item = tuple.Title;
		string item2 = tuple.Sub;
		TranslateTransform transform = (TranslateTransform)CarouselContent.RenderTransform;
		if (!animate)
		{
			CarouselContent.BeginAnimation(UIElement.OpacityProperty, null);
			transform.BeginAnimation(TranslateTransform.YProperty, null);
			CarouselContent.Opacity = 1.0;
			transform.Y = 0.0;
			TxtInfoHeadline.Text = item;
			TxtInfoSub.Text = item2;
			return;
		}
		DoubleAnimation doubleAnimation = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200L));
		DoubleAnimation animation = new DoubleAnimation(0.0, -15.0, TimeSpan.FromMilliseconds(200L));
		doubleAnimation.Completed += (object? _, EventArgs _) =>
		{
			TxtInfoHeadline.Text = _infoSlides[_currentSlide].Title;
			TxtInfoSub.Text = _infoSlides[_currentSlide].Sub;
			DoubleAnimation animation2 = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200L));
			DoubleAnimation animation3 = new DoubleAnimation(15.0, 0.0, TimeSpan.FromMilliseconds(200L));
			CarouselContent.BeginAnimation(UIElement.OpacityProperty, animation2);
			transform.BeginAnimation(TranslateTransform.YProperty, animation3);
		};
		CarouselContent.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
		transform.BeginAnimation(TranslateTransform.YProperty, animation);
	}
}
