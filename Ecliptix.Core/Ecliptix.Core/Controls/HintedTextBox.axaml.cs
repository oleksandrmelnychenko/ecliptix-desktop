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
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<HintedTextBox, string>(
        nameof(Text), string.Empty);

    public static readonly StyledProperty<string> WatermarkProperty = AvaloniaProperty.Register<HintedTextBox, string>(
        nameof(Watermark), string.Empty);

    public static readonly StyledProperty<string> HintProperty = AvaloniaProperty.Register<HintedTextBox, string>(
        nameof(Hint), string.Empty);

    public static readonly StyledProperty<char> PasswordCharProperty = AvaloniaProperty.Register<HintedTextBox, char>(
        nameof(PasswordChar));

    public static readonly StyledProperty<IBrush> FocusBorderBrushProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(
            nameof(FocusBorderBrush), new SolidColorBrush(Color.Parse("#6a5acd")));

    public static readonly StyledProperty<IBrush> TextForegroundProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(
            nameof(TextForeground), new SolidColorBrush(Colors.Black));

    public static readonly StyledProperty<IBrush> HintForegroundProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(
            nameof(HintForeground), new SolidColorBrush(Colors.Gray));

    public static readonly StyledProperty<DrawingImage?> IconRegularSourceProperty =
        AvaloniaProperty.Register<HintedTextBox, DrawingImage?>(nameof(IconRegularSource));

    public static readonly StyledProperty<DrawingImage?> IconErrorSourceProperty =
        AvaloniaProperty.Register<HintedTextBox, DrawingImage?>(nameof(IconErrorSource));

    public static readonly StyledProperty<string> ErrorTextProperty = AvaloniaProperty.Register<HintedTextBox, string>(
        nameof(ErrorText), string.Empty);

    public static readonly StyledProperty<double> EllipseOpacityProperty =
        AvaloniaProperty.Register<HintedTextBox, double>(nameof(EllipseOpacity));

    public static readonly StyledProperty<bool> HasErrorProperty = AvaloniaProperty.Register<HintedTextBox, bool>(
        nameof(HasError));

    public static readonly StyledProperty<ValidationType> ValidationTypeProperty =
        AvaloniaProperty.Register<HintedTextBox, ValidationType>(nameof(ValidationType));

    public static readonly StyledProperty<IBrush> MainBorderBrushProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(
            nameof(MainBorderBrush), new SolidColorBrush(Colors.LightGray));

    public static readonly StyledProperty<TextWrapping> TextWrappingProperty =
        AvaloniaProperty.Register<HintedTextBox, TextWrapping>(nameof(TextWrapping));

    public static readonly StyledProperty<int> MaxLengthProperty = AvaloniaProperty.Register<HintedTextBox, int>(
        nameof(MaxLength), int.MaxValue);

    public static readonly StyledProperty<int> RemainingCharactersProperty =
        AvaloniaProperty.Register<HintedTextBox, int>(nameof(RemainingCharacters), int.MaxValue);

    public static readonly StyledProperty<bool> ShowCharacterCounterProperty =
        AvaloniaProperty.Register<HintedTextBox, bool>(nameof(ShowCharacterCounter));

    public static readonly StyledProperty<bool> IsNumericOnlyProperty = AvaloniaProperty.Register<HintedTextBox, bool>(
        nameof(IsNumericOnly));

    public new static readonly StyledProperty<IBrush> BackgroundProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(
            nameof(Background), new SolidColorBrush(Colors.White));

    public new static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<HintedTextBox, double>(
            nameof(FontSize), 16.0);

    // New property for watermark font size
    public static readonly StyledProperty<double> WatermarkFontSizeProperty =
        AvaloniaProperty.Register<HintedTextBox, double>(
            nameof(WatermarkFontSize), 15.0);

    public new static readonly StyledProperty<FontWeight> FontWeightProperty =
        AvaloniaProperty.Register<HintedTextBox, FontWeight>(nameof(FontWeight), FontWeight.Normal);


    private static readonly SolidColorBrush ErrorBorderBrush = new(Color.Parse("#de1e31"));
    private static readonly SolidColorBrush FocusedBorderBrush = new(Color.Parse("#6a5acd"));
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    private readonly CompositeDisposable _disposables = new();
    private Panel? _counterPanel;
    private Border? _focusBorder;
    private Border? _mainBorder;
    private TextBox? _mainTextBox;
    private bool _isDirty;
    private bool _isInitialized;

    public event EventHandler<TextChangedEventArgs>? TextChanged;

    public DrawingImage? IconErrorSource
    {
        get => GetValue(IconErrorSourceProperty);
        set => SetValue(IconErrorSourceProperty, value);
    }

    public DrawingImage? IconRegularSource
    {
        get => GetValue(IconRegularSourceProperty);
        set => SetValue(IconRegularSourceProperty, value);
    }

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

    public double WatermarkFontSize
    {
        get => GetValue(WatermarkFontSizeProperty);
        set => SetValue(WatermarkFontSizeProperty, value);
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

    public bool ShowCharacterCounter
    {
        get => GetValue(ShowCharacterCounterProperty);
        set => SetValue(ShowCharacterCounterProperty, value);
    }

    public int RemainingCharacters
    {
        get => GetValue(RemainingCharactersProperty);
        private set => SetValue(RemainingCharactersProperty, value);
    }

    public int MaxLength
    {
        get => GetValue(MaxLengthProperty);
        set
        {
            SetValue(MaxLengthProperty, value);
            UpdateRemainingCharacters();
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
        private set => SetValue(HasErrorProperty, value);
    }

    public double EllipseOpacity
    {
        get => GetValue(EllipseOpacityProperty);
        private set => SetValue(EllipseOpacityProperty, value);
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
            UpdateRemainingCharacters();
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

    public HintedTextBox()
    {
        Loaded += OnControlLoaded;
        InitializeComponent();
    }

    private void OnControlLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;

        _isInitialized = true;
        Loaded -= OnControlLoaded;
        Initialize();
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (!_isDirty)
        {
            _isDirty = true;
            return;
        }

        if (_mainTextBox == null) return;

        string input = _mainTextBox.Text ?? string.Empty;

        if (IsNumericOnly)
        {
            input = FilterNumericInput(input);
        }

        UpdateTextValue(input);
        ValidateInput(input);
        UpdateBorderState();

        TextChanged?.Invoke(this, new TextChangedEventArgs(TextBox.TextChangedEvent));
    }

    private void OnGotFocus(object? sender, GotFocusEventArgs e) => UpdateBorderState(true);

    private void OnLostFocus(object? sender, RoutedEventArgs e) => UpdateBorderState();

    private void Initialize()
    {
        InitializeProperties();
        FindControls();

        if (!AreControlsValid()) return;

        SubscribeToEvents();
        SetupReactiveBindings();
        UpdateBorderState();
    }

    private void InitializeProperties()
    {
        RemainingCharacters = MaxLength;
        ErrorText = string.Empty;
        HasError = false;
        EllipseOpacity = 0.0;
    }

    private void FindControls()
    {
        this.FindControl<Panel>("ErrorStackPanel");
        _mainTextBox = this.FindControl<TextBox>("MainTextBox");
        _focusBorder = this.FindControl<Border>("FocusBorder");
        _mainBorder = this.FindControl<Border>("MainBorder");
        _counterPanel = this.FindControl<Panel>("CounterPanel");
    }

    private bool AreControlsValid()
    {
        return _mainTextBox != null && _focusBorder != null && _mainBorder != null;
    }

    private void SubscribeToEvents()
    {
        if (_mainTextBox == null) return;

        _mainTextBox.TextChanged += OnTextChanged;
        _mainTextBox.GotFocus += OnGotFocus;
        _mainTextBox.LostFocus += OnLostFocus;

        _disposables.Add(CreateEventUnsubscriber());
    }

    private IDisposable CreateEventUnsubscriber()
    {
        return Disposable.Create(() =>
        {
            if (_mainTextBox == null) return;

            _mainTextBox.TextChanged -= OnTextChanged;
            _mainTextBox.GotFocus -= OnGotFocus;
            _mainTextBox.LostFocus -= OnLostFocus;
        });
    }

    private void SetupReactiveBindings()
    {
        this.WhenAnyValue(x => x.ErrorText)
            .Subscribe(errorText =>
            {
                HasError = !string.IsNullOrEmpty(errorText);
                UpdateBorderState();
            })
            .DisposeWith(_disposables);
    }

    private string FilterNumericInput(string input)
    {
        string numeric = string.Concat(input.Where(char.IsDigit));

        if (numeric != input && _mainTextBox != null)
        {
            _mainTextBox.Text = numeric;
        }

        return numeric;
    }

    private void UpdateTextValue(string input)
    {
        SetValue(TextProperty, input);
        UpdateRemainingCharacters();
    }

    private void UpdateRemainingCharacters()
    {
        RemainingCharacters = MaxLength - (Text?.Length ?? 0);
    }

    private void ValidateInput(string input)
    {
        string validationMessage = MembershipValidation.Validate(ValidationType, input);

        if (!string.IsNullOrEmpty(validationMessage))
        {
            SetErrorState(validationMessage);
        }
        else
        {
            ClearErrorState();
        }
    }

    private void SetErrorState(string errorMessage)
    {
        ErrorText = errorMessage;
        EllipseOpacity = 1.0;
    }

    private void ClearErrorState()
    {
        ErrorText = string.Empty;
        EllipseOpacity = 0.0;
    }

    private void UpdateBorderState(bool forceFocus = false)
    {
        if (!AreControlsValid()) return;

        if (HasError)
        {
            SetBorderAppearance(ErrorBorderBrush, 1, TransparentBrush);
        }
        else if (forceFocus || _mainTextBox!.IsFocused)
        {
            SetBorderAppearance(FocusedBorderBrush, 1, TransparentBrush);
        }
        else
        {
            SetBorderAppearance(FocusedBorderBrush, 0, MainBorderBrush);
        }
    }

    private void SetBorderAppearance(IBrush focusBrush, double focusOpacity, IBrush mainBrush)
    {
        if (_focusBorder == null || _mainBorder == null) return;

        _focusBorder.BorderBrush = focusBrush;
        _focusBorder.Opacity = focusOpacity;
        _mainBorder.BorderBrush = mainBrush;
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