using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ReactiveUI;
using System.Reactive.Disposables;

namespace Ecliptix.Core.Controls;

public partial class HintedTextBox : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<string> WatermarkProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(Watermark), string.Empty);

    public static readonly StyledProperty<string> HintProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(Hint), string.Empty);

    public static readonly StyledProperty<char> PasswordCharProperty =
        AvaloniaProperty.Register<HintedTextBox, char>(nameof(PasswordChar), '\0');

    public static readonly StyledProperty<IBrush> FocusBorderBrushProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(nameof(FocusBorderBrush),
            new SolidColorBrush(Color.Parse("#6a5acd")));

    public static readonly StyledProperty<IBrush> TextForegroundProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(
            nameof(TextForeground),
            new SolidColorBrush(Colors.Black));

    public static readonly StyledProperty<IBrush> HintForegroundProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(
            nameof(HintForeground),
            new SolidColorBrush(Colors.Gray));

    public static readonly StyledProperty<string> IconSourceProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(IconSource), string.Empty);

    public static readonly StyledProperty<string> ErrorTextProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(ErrorText), string.Empty);

    public static readonly StyledProperty<double> EllipseOpacityProperty =
        AvaloniaProperty.Register<HintedTextBox, double>(nameof(EllipseOpacity), 0.0);

    public static readonly StyledProperty<bool> HasErrorProperty =
        AvaloniaProperty.Register<HintedTextBox, bool>(nameof(HasError), false);

    public static readonly StyledProperty<ValidationType> ValidationTypeProperty =
        AvaloniaProperty.Register<HintedTextBox, ValidationType>(nameof(ValidationType), ValidationType.None);
    
    
    public ValidationType ValidationType
    {
        get => GetValue(ValidationTypeProperty);
        set => SetValue(ValidationTypeProperty, value);
    }
    
    public bool HasError
    {
        get => GetValue(HasErrorProperty);
        set => SetValue(HasErrorProperty, value);
    }

    public double EllipseOpacity
    {
        get => GetValue(EllipseOpacityProperty);
        set => SetValue(EllipseOpacityProperty, value);
    }

    public string IconSource
    {
        get => GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public IBrush TextForeground
    {
        get => GetValue(TextForegroundProperty);
        set => SetValue(TextForegroundProperty, value);
    }

    public IBrush HintForeground
    {
        get => GetValue(HintForegroundProperty);
        set => SetValue(HintForegroundProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public string Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public char PasswordChar
    {
        get => GetValue(PasswordCharProperty);
        set => SetValue(PasswordCharProperty, value);
    }

    public IBrush FocusBorderBrush
    {
        get => GetValue(FocusBorderBrushProperty);
        set => SetValue(FocusBorderBrushProperty, value);
    }

    public string ErrorText
    {
        get => GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }

    private TextBox? _mainTextBox;
    private Border? _focusBorder;
    private Border? _mainBorder;
    private readonly CompositeDisposable _disposables = new();
    private bool _isDirty = false;
    
    public HintedTextBox()
    {
        InitializeComponent();
        Initialize();
    }

    private void Initialize()
    {
        // Set initial state
        ErrorText = string.Empty;
        HasError = false;
        EllipseOpacity = 0.0;

        // Find controls
        _mainTextBox = this.FindControl<TextBox>("MainTextBox");
        _focusBorder = this.FindControl<Border>("FocusBorder");
        _mainBorder = this.FindControl<Border>("MainBorder");

        if (_mainTextBox == null || _focusBorder == null || _mainBorder == null)
        {
            // Log or handle missing controls (for debugging purposes)
            return;
        }

        // Define event handlers for subscription and unsubscription
        void OnTextChanged(object? sender, EventArgs e)
        {
            if (!_isDirty)
            {
                _isDirty = true;
                return;
            }
            
            string input = _mainTextBox.Text ?? string.Empty;
            string? validationMessage = InputValidator.Validate(input, ValidationType);
            
            if (!string.IsNullOrEmpty(validationMessage))
            {
                ErrorText = validationMessage;
                EllipseOpacity = 1.0;
            }
            else
            {
                ErrorText = string.Empty;
                EllipseOpacity = 0.0;
            }


            UpdateBorderState();
        }

        void OnGotFocus(object? sender, GotFocusEventArgs e)
        {
            UpdateBorderState(true);
        }

        void OnLostFocus(object? sender, RoutedEventArgs e)
        {
            UpdateBorderState();
        }

        _mainTextBox.TextChanged += OnTextChanged;
        _mainTextBox.GotFocus += OnGotFocus;
        _mainTextBox.LostFocus += OnLostFocus;

        _disposables.Add(Disposable.Create(() =>
        {
            _mainTextBox.TextChanged -= OnTextChanged;
            _mainTextBox.GotFocus -= OnGotFocus;
            _mainTextBox.LostFocus -= OnLostFocus;
        }));

        this.WhenAnyValue(x => x.ErrorText)
            .Subscribe(errorText =>
            {
                HasError = !string.IsNullOrEmpty(errorText);
                UpdateBorderState();
            })
            .DisposeWith(_disposables);

        UpdateBorderState();
    }

    private void UpdateBorderState(bool forceFocus = false)
    {
        if (_mainTextBox == null || _focusBorder == null || _mainBorder == null)
        {
            return;
        }

        if (HasError)
        {
            _focusBorder.BorderBrush = new SolidColorBrush(Color.Parse("#de1e31"));
            _focusBorder.Opacity = 1;
            _mainBorder.BorderBrush = new SolidColorBrush(Colors.Transparent);
        }
        else if (forceFocus || _mainTextBox.IsFocused)
        {
            _focusBorder.BorderBrush = new SolidColorBrush(Color.Parse("#6a5acd"));
            _focusBorder.Opacity = 1;
            _mainBorder.BorderBrush = new SolidColorBrush(Colors.Transparent);
        }
        else
        {
            _focusBorder.BorderBrush = new SolidColorBrush(Color.Parse("#6a5acd"));
            _focusBorder.Opacity = 0;
            _mainBorder.BorderBrush = new SolidColorBrush(Color.Parse("#808080"));
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _disposables.Dispose();
    }
}