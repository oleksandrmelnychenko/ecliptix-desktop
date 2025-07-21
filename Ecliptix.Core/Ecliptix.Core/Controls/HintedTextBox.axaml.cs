using System;
using System.Linq;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Ecliptix.Utilities.Membership;
using ReactiveUI;

namespace Ecliptix.Core.Controls;

public partial class HintedTextBox : UserControl
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<
        HintedTextBox,
        string
    >(nameof(Text), string.Empty);

    public static readonly StyledProperty<string> WatermarkProperty = AvaloniaProperty.Register<
        HintedTextBox,
        string
    >(nameof(Watermark), string.Empty);

    public static readonly StyledProperty<string> HintProperty = AvaloniaProperty.Register<
        HintedTextBox,
        string
    >(nameof(Hint), string.Empty);

    public static readonly StyledProperty<char> PasswordCharProperty = AvaloniaProperty.Register<
        HintedTextBox,
        char
    >(nameof(PasswordChar));

    public static readonly StyledProperty<IBrush> FocusBorderBrushProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(
            nameof(FocusBorderBrush),
            new SolidColorBrush(Color.Parse("#6a5acd"))
        );

    public static readonly StyledProperty<IBrush> TextForegroundProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(
            nameof(TextForeground),
            new SolidColorBrush(Colors.Black)
        );

    public static readonly StyledProperty<IBrush> HintForegroundProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(
            nameof(HintForeground),
            new SolidColorBrush(Colors.Gray)
        );

    public static readonly StyledProperty<string> IconSourceProperty = AvaloniaProperty.Register<
        HintedTextBox,
        string
    >(nameof(IconSource), string.Empty);

    public static readonly StyledProperty<string> ErrorTextProperty = AvaloniaProperty.Register<
        HintedTextBox,
        string
    >(nameof(ErrorText), string.Empty);

    public static readonly StyledProperty<double> EllipseOpacityProperty =
        AvaloniaProperty.Register<HintedTextBox, double>(nameof(EllipseOpacity));

    public static readonly StyledProperty<bool> HasErrorProperty = AvaloniaProperty.Register<
        HintedTextBox,
        bool
    >(nameof(HasError));

    public static readonly StyledProperty<ValidationType> ValidationTypeProperty =
        AvaloniaProperty.Register<HintedTextBox, ValidationType>(nameof(ValidationType));

    public static readonly StyledProperty<IBrush> MainBorderBrushProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(
            nameof(MainBorderBrush),
            new SolidColorBrush(Colors.LightGray)
        );

    public static readonly StyledProperty<TextWrapping> TextWrappingProperty =
        AvaloniaProperty.Register<HintedTextBox, TextWrapping>(nameof(TextWrapping));

    public static readonly StyledProperty<int> MaxLengthProperty = AvaloniaProperty.Register<
        HintedTextBox,
        int
    >(nameof(MaxLength), int.MaxValue);

    public static readonly StyledProperty<int> RemainingCharactersProperty =
        AvaloniaProperty.Register<HintedTextBox, int>(nameof(RemainingCharacters), int.MaxValue);

    public static readonly StyledProperty<bool> ShowCharacterCounterProperty =
        AvaloniaProperty.Register<HintedTextBox, bool>(nameof(ShowCharacterCounter));

    public static readonly StyledProperty<bool> IsNumericOnlyProperty = AvaloniaProperty.Register<
        HintedTextBox,
        bool
    >(nameof(IsNumericOnly));

    public new static readonly StyledProperty<IBrush> BackgroundProperty = AvaloniaProperty.Register<
        HintedTextBox,
        IBrush
    >(nameof(Background), new SolidColorBrush(Colors.White));

    public new static readonly StyledProperty<double> FontSizeProperty = AvaloniaProperty.Register<
        HintedTextBox,
        double
    >(nameof(FontSize), 14.0);

    public new static readonly StyledProperty<FontWeight> FontWeightProperty =
        AvaloniaProperty.Register<HintedTextBox, FontWeight>(nameof(FontWeight), FontWeight.Normal);

    public new FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public new double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public new IBrush Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public bool IsNumericOnly
    {
        get => GetValue(IsNumericOnlyProperty);
        set => SetValue(IsNumericOnlyProperty, value);
    }

    private readonly CompositeDisposable _disposables = new();
    private Panel? _counterPanel;
    private Border? _focusBorder;
    private bool _isDirty;
    private Border? _mainBorder;
    private Panel? _errorStackPanel;

    private TextBox? _mainTextBox;

    public event EventHandler<TextChangedEventArgs>? TextChanged;

    // public HintedTextBox()
    // {
    //     InitializeComponent();
    //     Initialize();
    // }

    private bool _isInitialized;

    public HintedTextBox()
    {
        Loaded += OnControlLoaded;
        InitializeComponent();
    }

    private void OnControlLoaded(object? sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
        {
            _isInitialized = true;
            Loaded -= OnControlLoaded;
            Initialize();
        }
    }

    public bool ShowCharacterCounter
    {
        get => GetValue(ShowCharacterCounterProperty);
        set => SetValue(ShowCharacterCounterProperty, value);
    }

    public int RemainingCharacters
    {
        get => GetValue(RemainingCharactersProperty);
        set => SetValue(RemainingCharactersProperty, value);
    }

    public int MaxLength
    {
        get => GetValue(MaxLengthProperty);
        set
        {
            SetValue(MaxLengthProperty, value);
            RemainingCharacters = value - (Text?.Length ?? 0);
        }
    }

    public TextWrapping TextWrapping
    {
        get => GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public IBrush MainBorderBrush
    {
        get => GetValue(MainBorderBrushProperty);
        set => SetValue(MainBorderBrushProperty, value);
    }

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
        set
        {
            SetValue(TextProperty, value);
            RemainingCharacters = MaxLength - (value?.Length ?? 0);
        }
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

    private void Initialize()
    {
        RemainingCharacters = MaxLength;
        
        ErrorText = string.Empty;
        HasError = false;
        EllipseOpacity = 0.0;

 
        _errorStackPanel = this.FindControl<Panel>("ErrorStackPanel");
        _mainTextBox = this.FindControl<TextBox>("MainTextBox");
        _focusBorder = this.FindControl<Border>("FocusBorder");
        _mainBorder = this.FindControl<Border>("MainBorder");
        _counterPanel = this.FindControl<Panel>("CounterPanel");

        if (_mainTextBox == null || _focusBorder == null || _mainBorder == null)
            return;

        void OnTextChanged(object? sender, EventArgs e)
        {
            if (!_isDirty)
            {
                _isDirty = true;
                return;
            }

            string input = _mainTextBox.Text ?? string.Empty;

            if (IsNumericOnly)
            {
                string numeric = string.Concat(input.Where(char.IsDigit));
                if (numeric != input)
                {
                    _mainTextBox.Text = numeric;
                    input = numeric;
                }
            }

            SetValue(TextProperty, input);

            RemainingCharacters = MaxLength - input.Length;

            string? validationMessage = MembershipValidation.Validate(ValidationType,input);
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

            TextChanged?.Invoke(this, new TextChangedEventArgs(TextBox.TextChangedEvent));
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

        _disposables.Add(
            Disposable.Create(() =>
            {
                _mainTextBox.TextChanged -= OnTextChanged;
                _mainTextBox.GotFocus -= OnGotFocus;
                _mainTextBox.LostFocus -= OnLostFocus;
            })
        );

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
            return;

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
            _mainBorder.BorderBrush = MainBorderBrush;
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
