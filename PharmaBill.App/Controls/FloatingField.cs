using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PharmaBill.App.Controls;

/// <summary>
/// Wraps one input (TextBox, ComboBox, DatePicker or PasswordBox) with an always-visible floating label,
/// required marker, helper text and validation message.
/// </summary>
public class FloatingField : ContentControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(FloatingField), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsRequiredProperty =
        DependencyProperty.Register(nameof(IsRequired), typeof(bool), typeof(FloatingField), new PropertyMetadata(false));

    public static readonly DependencyProperty HelperTextProperty =
        DependencyProperty.Register(nameof(HelperText), typeof(string), typeof(FloatingField), new PropertyMetadata(string.Empty, (d, _) => ((FloatingField)d).Refresh()));

    public static readonly DependencyProperty ErrorTextProperty =
        DependencyProperty.Register(
            nameof(ErrorText),
            typeof(string),
            typeof(FloatingField),
            new PropertyMetadata(string.Empty, (d, _) => ((FloatingField)d).Refresh()));

    public static readonly DependencyProperty EnterMovesNextProperty =
        DependencyProperty.Register(nameof(EnterMovesNext), typeof(bool), typeof(FloatingField), new PropertyMetadata(true));

    private static readonly DependencyPropertyKey HasValuePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasValue), typeof(bool), typeof(FloatingField), new PropertyMetadata(false));

    private static readonly DependencyPropertyKey HasErrorPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasError), typeof(bool), typeof(FloatingField), new PropertyMetadata(false));

    private static readonly DependencyPropertyKey IsContentReadOnlyPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsContentReadOnly), typeof(bool), typeof(FloatingField), new PropertyMetadata(false));

    private static readonly DependencyPropertyKey ShowHelperPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(ShowHelper), typeof(bool), typeof(FloatingField), new PropertyMetadata(false));

    public static readonly DependencyProperty ShowHelperProperty = ShowHelperPropertyKey.DependencyProperty;
    public static readonly DependencyProperty HasValueProperty = HasValuePropertyKey.DependencyProperty;
    public static readonly DependencyProperty HasErrorProperty = HasErrorPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsContentReadOnlyProperty = IsContentReadOnlyPropertyKey.DependencyProperty;

    static FloatingField()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FloatingField), new FrameworkPropertyMetadata(typeof(FloatingField)));
        IsTabStopProperty.OverrideMetadata(typeof(FloatingField), new FrameworkPropertyMetadata(false));
    }

    public FloatingField()
    {
        AddHandler(TextBoxBase.TextChangedEvent, new RoutedEventHandler((_, _) => Refresh()));
        AddHandler(Selector.SelectionChangedEvent, new RoutedEventHandler((_, _) => Refresh()));
        AddHandler(PasswordBox.PasswordChangedEvent, new RoutedEventHandler((_, _) => Refresh()));
        AddHandler(DatePicker.SelectedDateChangedEvent, new RoutedEventHandler((_, _) => Refresh()));
        Loaded += (_, _) =>
        {
            ApplyInnerStyle();
            Refresh();
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public bool IsRequired { get => (bool)GetValue(IsRequiredProperty); set => SetValue(IsRequiredProperty, value); }
    public string HelperText { get => (string)GetValue(HelperTextProperty); set => SetValue(HelperTextProperty, value); }
    public string ErrorText { get => (string)GetValue(ErrorTextProperty); set => SetValue(ErrorTextProperty, value); }
    public bool EnterMovesNext { get => (bool)GetValue(EnterMovesNextProperty); set => SetValue(EnterMovesNextProperty, value); }
    public bool HasValue => (bool)GetValue(HasValueProperty);
    public bool ShowHelper => (bool)GetValue(ShowHelperProperty);
    public bool HasError => (bool)GetValue(HasErrorProperty);
    public bool IsContentReadOnly => (bool)GetValue(IsContentReadOnlyProperty);

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        ApplyInnerStyle();
        Refresh();
    }

    private void ApplyInnerStyle()
    {
        if (Content is not FrameworkElement element || element.Style is not null ||
            element.ReadLocalValue(StyleProperty) != DependencyProperty.UnsetValue)
        {
            return;
        }

        var key = element switch
        {
            PasswordBox => "FloatingInnerPasswordBox",
            TextBox => "FloatingInnerTextBox",
            ComboBox => "FloatingInnerComboBox",
            DatePicker => "FloatingInnerDatePicker",
            _ => null
        };
        if (key is not null && (TryFindResource(key) ?? Application.Current?.TryFindResource(key)) is Style style)
        {
            element.Style = style;
        }
    }

    private void Refresh()
    {
        var hasValue = Content switch
        {
            PasswordBox box => box.Password.Length > 0,
            TextBox box => box.Text.Length > 0,
            ComboBox box => box.SelectedItem is not null || (box.IsEditable && !string.IsNullOrEmpty(box.Text)),
            DatePicker picker => picker.SelectedDate is not null,
            _ => false
        };
        var readOnly = Content switch
        {
            TextBox box => box.IsReadOnly,
            _ => false
        };
        SetValue(HasValuePropertyKey, hasValue);
        SetValue(IsContentReadOnlyPropertyKey, readOnly);
        var hasError = !string.IsNullOrEmpty(ErrorText);
        SetValue(HasErrorPropertyKey, hasError);
        SetValue(ShowHelperPropertyKey, !hasError && !string.IsNullOrEmpty(HelperText));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !EnterMovesNext || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        var canMove = e.OriginalSource switch
        {
            TextBox box => !box.AcceptsReturn,
            PasswordBox => true,
            ComboBox box => !box.IsDropDownOpen,
            _ => false
        };
        if (canMove && Keyboard.FocusedElement is UIElement focused)
        {
            focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }
}