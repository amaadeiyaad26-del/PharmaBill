using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PharmaBill.App.Controls;

public class FloatingField : ContentControl
{
	public static readonly DependencyProperty LabelProperty;

	public static readonly DependencyProperty IsRequiredProperty;

	public static readonly DependencyProperty HelperTextProperty;

	public static readonly DependencyProperty ErrorTextProperty;

	public static readonly DependencyProperty EnterMovesNextProperty;

	private static readonly DependencyPropertyKey HasValuePropertyKey;

	private static readonly DependencyPropertyKey HasErrorPropertyKey;

	private static readonly DependencyPropertyKey IsContentReadOnlyPropertyKey;

	private static readonly DependencyPropertyKey ShowHelperPropertyKey;

	public static readonly DependencyProperty ShowHelperProperty;

	public static readonly DependencyProperty HasValueProperty;

	public static readonly DependencyProperty HasErrorProperty;

	public static readonly DependencyProperty IsContentReadOnlyProperty;

	public string Label
	{
		get
		{
			return (string)GetValue(LabelProperty);
		}
		set
		{
			SetValue(LabelProperty, value);
		}
	}

	public bool IsRequired
	{
		get
		{
			return (bool)GetValue(IsRequiredProperty);
		}
		set
		{
			SetValue(IsRequiredProperty, value);
		}
	}

	public string HelperText
	{
		get
		{
			return (string)GetValue(HelperTextProperty);
		}
		set
		{
			SetValue(HelperTextProperty, value);
		}
	}

	public string ErrorText
	{
		get
		{
			return (string)GetValue(ErrorTextProperty);
		}
		set
		{
			SetValue(ErrorTextProperty, value);
		}
	}

	public bool EnterMovesNext
	{
		get
		{
			return (bool)GetValue(EnterMovesNextProperty);
		}
		set
		{
			SetValue(EnterMovesNextProperty, value);
		}
	}

	public bool HasValue => (bool)GetValue(HasValueProperty);

	public bool ShowHelper => (bool)GetValue(ShowHelperProperty);

	public bool HasError => (bool)GetValue(HasErrorProperty);

	public bool IsContentReadOnly => (bool)GetValue(IsContentReadOnlyProperty);

	static FloatingField()
	{
		LabelProperty = DependencyProperty.Register("Label", typeof(string), typeof(FloatingField), new PropertyMetadata(string.Empty));
		IsRequiredProperty = DependencyProperty.Register("IsRequired", typeof(bool), typeof(FloatingField), new PropertyMetadata(false));
		HelperTextProperty = DependencyProperty.Register("HelperText", typeof(string), typeof(FloatingField), new PropertyMetadata(string.Empty, (DependencyObject d, DependencyPropertyChangedEventArgs _) =>
		{
			((FloatingField)d).Refresh();
		}));
		ErrorTextProperty = DependencyProperty.Register("ErrorText", typeof(string), typeof(FloatingField), new PropertyMetadata(string.Empty, (DependencyObject d, DependencyPropertyChangedEventArgs _) =>
		{
			((FloatingField)d).Refresh();
		}));
		EnterMovesNextProperty = DependencyProperty.Register("EnterMovesNext", typeof(bool), typeof(FloatingField), new PropertyMetadata(true));
		HasValuePropertyKey = DependencyProperty.RegisterReadOnly("HasValue", typeof(bool), typeof(FloatingField), new PropertyMetadata(false));
		HasErrorPropertyKey = DependencyProperty.RegisterReadOnly("HasError", typeof(bool), typeof(FloatingField), new PropertyMetadata(false));
		IsContentReadOnlyPropertyKey = DependencyProperty.RegisterReadOnly("IsContentReadOnly", typeof(bool), typeof(FloatingField), new PropertyMetadata(false));
		ShowHelperPropertyKey = DependencyProperty.RegisterReadOnly("ShowHelper", typeof(bool), typeof(FloatingField), new PropertyMetadata(false));
		ShowHelperProperty = ShowHelperPropertyKey.DependencyProperty;
		HasValueProperty = HasValuePropertyKey.DependencyProperty;
		HasErrorProperty = HasErrorPropertyKey.DependencyProperty;
		IsContentReadOnlyProperty = IsContentReadOnlyPropertyKey.DependencyProperty;
		FrameworkElement.DefaultStyleKeyProperty.OverrideMetadata(typeof(FloatingField), new FrameworkPropertyMetadata(typeof(FloatingField)));
		Control.IsTabStopProperty.OverrideMetadata(typeof(FloatingField), new FrameworkPropertyMetadata(false));
	}

	public FloatingField()
	{
		AddHandler(TextBoxBase.TextChangedEvent, (RoutedEventHandler)((object _, RoutedEventArgs _) =>
		{
			Refresh();
		}));
		AddHandler(Selector.SelectionChangedEvent, (RoutedEventHandler)((object _, RoutedEventArgs _) =>
		{
			Refresh();
		}));
		AddHandler(PasswordBox.PasswordChangedEvent, (RoutedEventHandler)((object _, RoutedEventArgs _) =>
		{
			Refresh();
		}));
		AddHandler(DatePicker.SelectedDateChangedEvent, (RoutedEventHandler)((object _, RoutedEventArgs _) =>
		{
			Refresh();
		}));
		Loaded += (object _, RoutedEventArgs _) =>
		{
			ApplyInnerStyle();
			Refresh();
		};
		PreviewKeyDown += OnPreviewKeyDown;
	}

	protected override void OnContentChanged(object oldContent, object newContent)
	{
		base.OnContentChanged(oldContent, newContent);
		ApplyInnerStyle();
		Refresh();
	}

	private void ApplyInnerStyle()
	{
		FrameworkElement frameworkElement = ResolveInputElement();
		if (frameworkElement != null && frameworkElement.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue)
		{
			string text;
			if (frameworkElement is PasswordBox)
			{
				text = "FloatingInnerPasswordBox";
			}
			else if (frameworkElement is TextBox)
			{
				text = "FloatingInnerTextBox";
			}
			else if (frameworkElement is ComboBox)
			{
				text = "FloatingInnerComboBox";
			}
			else
			{
				text = ((!(frameworkElement is DatePicker)) ? null : "FloatingInnerDatePicker");
			}
			string text2 = text;
			if (text2 != null && (TryFindResource(text2) ?? Application.Current?.TryFindResource(text2)) is Style style)
			{
				frameworkElement.Style = style;
			}
		}
	}

	private void Refresh()
	{
		FrameworkElement frameworkElement = ResolveInputElement();
		bool flag;
		if (frameworkElement is PasswordBox passwordBox)
		{
			flag = passwordBox.Password.Length > 0;
		}
		else if (frameworkElement is TextBox textBox)
		{
			flag = textBox.Text.Length > 0;
		}
		else
		{
			flag = ((frameworkElement is ComboBox comboBox) ? (comboBox.SelectedItem != null || (comboBox.IsEditable && !string.IsNullOrEmpty(comboBox.Text))) : (frameworkElement is DatePicker { SelectedDate: var selectedDate } && selectedDate.HasValue));
		}
		bool flag2 = flag;
		bool flag3 = frameworkElement is TextBox textBox2 && textBox2.IsReadOnly;
		SetValue(HasValuePropertyKey, flag2);
		SetValue(IsContentReadOnlyPropertyKey, flag3);
		bool flag4 = !string.IsNullOrEmpty(ErrorText);
		SetValue(HasErrorPropertyKey, flag4);
		SetValue(ShowHelperPropertyKey, !flag4 && !string.IsNullOrEmpty(HelperText));
	}

	private FrameworkElement? ResolveInputElement()
	{
		object content = Content;
		if (!(content is PasswordBox) && !(content is TextBox) && !(content is ComboBox) && !(content is DatePicker))
		{
			if (!(content is Panel panel))
			{
				if (content is Decorator { Child: FrameworkElement child })
				{
					bool flag = ((child is PasswordBox || child is TextBox || child is ComboBox || child is DatePicker) ? true : false);
					return flag ? child : null;
				}
				return null;
			}
			return panel.Children.OfType<FrameworkElement>().FirstOrDefault((FrameworkElement frameworkElement) => (frameworkElement is PasswordBox || frameworkElement is TextBox || frameworkElement is ComboBox || frameworkElement is DatePicker) ? true : false);
		}
		return (FrameworkElement)Content;
	}

	private void OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return && EnterMovesNext && Keyboard.Modifiers == ModifierKeys.None)
		{
			object originalSource = e.OriginalSource;
			bool flag = ((originalSource is TextBox textBox) ? (!textBox.AcceptsReturn) : (originalSource is PasswordBox || (originalSource is ComboBox comboBox && !comboBox.IsDropDownOpen)));
			if (flag && Keyboard.FocusedElement is UIElement uIElement)
			{
				uIElement.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
				e.Handled = true;
			}
		}
	}
}
