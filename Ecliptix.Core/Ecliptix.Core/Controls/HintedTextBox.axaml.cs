using System;
using System.Linq;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Ecliptix.Utilities.Membership;
using ReactiveUI;

namespace Ecliptix.Core.Controls;

public sealed partial class HintedTextBox : UserControl, IDisposable
{
    public static readonly StyledProperty<bool> IsPasswordModeProperty =
        AvaloniaProperty.Register<HintedTextBox, bool>(nameof(IsPasswordMode), false);

    public static readonly StyledProperty<char> PasswordMaskCharProperty =
        AvaloniaProperty.Register<HintedTextBox, char>(nameof(PasswordMaskChar), '●');

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(Text), string.Empty,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string> WatermarkProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(Watermark), string.Empty);

    public static readonly StyledProperty<string> HintProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(Hint), string.Empty);

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

    public static readonly StyledProperty<string> ErrorTextProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(ErrorText), string.Empty);

    public static readonly StyledProperty<double> EllipseOpacityProperty =
        AvaloniaProperty.Register<HintedTextBox, double>(nameof(EllipseOpacity));

    public static readonly StyledProperty<bool> HasErrorProperty =
        AvaloniaProperty.Register<HintedTextBox, bool>(nameof(HasError));

    public static readonly StyledProperty<ValidationType> ValidationTypeProperty =
        AvaloniaProperty.Register<HintedTextBox, ValidationType>(nameof(ValidationType));

    public static readonly StyledProperty<IBrush> MainBorderBrushProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(
            nameof(MainBorderBrush), new SolidColorBrush(Colors.LightGray));

    public static readonly StyledProperty<TextWrapping> TextWrappingProperty =
        AvaloniaProperty.Register<HintedTextBox, TextWrapping>(nameof(TextWrapping));

    public static readonly StyledProperty<int> MaxLengthProperty =
        AvaloniaProperty.Register<HintedTextBox, int>(nameof(MaxLength), int.MaxValue);

    public static readonly StyledProperty<int> RemainingCharactersProperty =
        AvaloniaProperty.Register<HintedTextBox, int>(nameof(RemainingCharacters), int.MaxValue);

    public static readonly StyledProperty<bool> ShowCharacterCounterProperty =
        AvaloniaProperty.Register<HintedTextBox, bool>(nameof(ShowCharacterCounter));

    public static readonly StyledProperty<bool> IsNumericOnlyProperty =
        AvaloniaProperty.Register<HintedTextBox, bool>(nameof(IsNumericOnly));

    public new static readonly StyledProperty<IBrush> BackgroundProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(
            nameof(Background), new SolidColorBrush(Colors.White));

    public new static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<HintedTextBox, double>(nameof(FontSize), 16.0);

    public static readonly StyledProperty<double> WatermarkFontSizeProperty =
        AvaloniaProperty.Register<HintedTextBox, double>(nameof(WatermarkFontSize), 15.0);

    public new static readonly StyledProperty<FontWeight> FontWeightProperty =
        AvaloniaProperty.Register<HintedTextBox, FontWeight>(nameof(FontWeight), FontWeight.Normal);

    public static readonly RoutedEvent<PasswordCharactersAddedEventArgs> PasswordCharactersAddedEvent =
        RoutedEvent.Register<HintedTextBox, PasswordCharactersAddedEventArgs>(nameof(PasswordCharactersAdded),
            RoutingStrategies.Bubble);

    public static readonly RoutedEvent<PasswordCharactersRemovedEventArgs> PasswordCharactersRemovedEvent =
        RoutedEvent.Register<HintedTextBox, PasswordCharactersRemovedEventArgs>(nameof(PasswordCharactersRemoved),
            RoutingStrategies.Bubble);

    public event EventHandler<PasswordCharactersAddedEventArgs> PasswordCharactersAdded
    {
        add => AddHandler(PasswordCharactersAddedEvent, value);
        remove => RemoveHandler(PasswordCharactersAddedEvent, value);
    }

    public event EventHandler<PasswordCharactersRemovedEventArgs> PasswordCharactersRemoved
    {
        add => AddHandler(PasswordCharactersRemovedEvent, value);
        remove => RemoveHandler(PasswordCharactersRemovedEvent, value);
    }

    public bool IsPasswordMode
    {
        get => GetValue(IsPasswordModeProperty);
        set => SetValue(IsPasswordModeProperty, value);
    }

    public char PasswordMaskChar
    {
        get => GetValue(PasswordMaskCharProperty);
        set => SetValue(PasswordMaskCharProperty, value);
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

    public IBrush FocusBorderBrush
    {
        get => GetValue(FocusBorderBrushProperty);
        set => SetValue(FocusBorderBrushProperty, value);
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

    public DrawingImage? IconRegularSource
    {
        get => GetValue(IconRegularSourceProperty);
        set => SetValue(IconRegularSourceProperty, value);
    }

    public DrawingImage? IconErrorSource
    {
        get => GetValue(IconErrorSourceProperty);
        set => SetValue(IconErrorSourceProperty, value);
    }

    public string ErrorText
    {
        get => GetValue(ErrorTextProperty);
        private set => SetValue(ErrorTextProperty, value);
    }

    public double EllipseOpacity
    {
        get => GetValue(EllipseOpacityProperty);
        private set => SetValue(EllipseOpacityProperty, value);
    }

    public bool HasError
    {
        get => GetValue(HasErrorProperty);
        private set => SetValue(HasErrorProperty, value);
    }

    public ValidationType ValidationType
    {
        get => GetValue(ValidationTypeProperty);
        set => SetValue(ValidationTypeProperty, value);
    }

    public IBrush MainBorderBrush
    {
        get => GetValue(MainBorderBrushProperty);
        set => SetValue(MainBorderBrushProperty, value);
    }

    public TextWrapping TextWrapping
    {
        get => GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public int MaxLength
    {
        get => GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    public int RemainingCharacters
    {
        get => GetValue(RemainingCharactersProperty);
        private set => SetValue(RemainingCharactersProperty, value);
    }

    public bool ShowCharacterCounter
    {
        get => GetValue(ShowCharacterCounterProperty);
        set => SetValue(ShowCharacterCounterProperty, value);
    }

    public bool IsNumericOnly
    {
        get => GetValue(IsNumericOnlyProperty);
        set => SetValue(IsNumericOnlyProperty, value);
    }

    public new IBrush Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
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

    public new FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    private readonly CompositeDisposable _disposables = new();
    private TextBox? _mainTextBox;
    private TextBlock? _passwordMaskOverlay;
    private Border? _focusBorder;
    private Border? _mainBorder;
    private string _shadowText = string.Empty;
    private bool _isUpdatingFromCode;
    private bool _isDisposed;
    private bool _isControlInitialized;
    private int _nextCaretPosition;

    public HintedTextBox()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    public void SyncPasswordState(int newPasswordLength)
    {
        _shadowText = new string(PasswordMaskChar, newPasswordLength);
        UpdateTextBox(_shadowText, _nextCaretPosition);
        UpdatePasswordMaskOverlay(newPasswordLength);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_isControlInitialized || _isDisposed) return;
        Initialize();
    }

    private void Initialize()
    {
        if (_isControlInitialized) return;

        FindControls();
        if (_mainTextBox == null) return;

        _mainTextBox.TextChanged += OnTextChanged;
        _mainTextBox.GotFocus += OnGotFocus;
        _mainTextBox.LostFocus += OnLostFocus;
        _disposables.Add(Disposable.Create(() =>
        {
            if (_mainTextBox == null) return;
            _mainTextBox.TextChanged -= OnTextChanged;
            _mainTextBox.GotFocus -= OnGotFocus;
            _mainTextBox.LostFocus -= OnLostFocus;
        }));

        SetupReactiveBindings();
        UpdateBorderState();
        _isControlInitialized = true;
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingFromCode || _mainTextBox == null || _isDisposed) return;

        ProcessTextChange();
    }

    private void ProcessTextChange()
    {
        if (_isUpdatingFromCode || _mainTextBox == null || _isDisposed) return;

        if (IsPasswordMode)
        {
            string newText = _mainTextBox!.Text ?? string.Empty;
            if (newText == _shadowText) return;

            (int diffIndex, int removedCount, string added) = Diff(_shadowText, newText);

            if (removedCount > 0 && string.IsNullOrEmpty(added))
            {
                diffIndex = _mainTextBox.CaretIndex;
            }

            _nextCaretPosition = diffIndex + added.Length;

            if (removedCount > 0)
            {
                RaiseEvent(new PasswordCharactersRemovedEventArgs(PasswordCharactersRemovedEvent, diffIndex,
                    removedCount));
            }

            if (!string.IsNullOrEmpty(added))
            {
                RaiseEvent(new PasswordCharactersAddedEventArgs(PasswordCharactersAddedEvent, diffIndex, added));
            }
        }
        else
        {
            ProcessStandardChange();
        }
    }

    private void ProcessStandardChange()
    {
        string input = _mainTextBox!.Text ?? string.Empty;

        if (IsNumericOnly)
        {
            string filtered = string.Concat(input.Where(char.IsDigit));
            if (input != filtered)
            {
                int caret = _mainTextBox.CaretIndex - (input.Length - filtered.Length);
                UpdateTextBox(filtered, caret);
                return;
            }
        }

        Text = input;
        UpdateRemainingCharacters();
        ValidateInput(input);
    }

    private static (int Index, int RemovedCount, string Added) Diff(string oldStr, string newStr)
    {
        int prefixLength = oldStr.TakeWhile((c, i) => i < newStr.Length && c == newStr[i]).Count();
        int suffixLength = oldStr.Reverse().TakeWhile((c, i) =>
            i < newStr.Length - prefixLength && c == newStr[^(i + 1)]).Count();
        int removedCount = Math.Max(0, oldStr.Length - prefixLength - suffixLength);
        string added = newStr.Substring(prefixLength, Math.Max(0, newStr.Length - prefixLength - suffixLength));
        return (prefixLength, removedCount, added);
    }

    private void UpdateTextBox(string text, int caretIndex)
    {
        if (_mainTextBox == null) return;

        _isUpdatingFromCode = true;
        _mainTextBox.Text = text;

        _mainTextBox.CaretIndex = Math.Clamp(caretIndex, 0, text.Length);

        _isUpdatingFromCode = false;
    }

    private void UpdatePasswordMaskOverlay(int length)
    {
        if (_passwordMaskOverlay != null)
        {
            _passwordMaskOverlay.Text = new string(PasswordMaskChar, length);
        }
    }

    private void UpdateBorderState()
    {
        if (_mainTextBox == null || _focusBorder == null || _mainBorder == null) return;

        bool isFocused = _mainTextBox.IsFocused;
        if (HasError)
        {
            _focusBorder.BorderBrush = new SolidColorBrush(Color.Parse("#de1e31"));
            _focusBorder.Opacity = 1;
            _mainBorder.BorderBrush = new SolidColorBrush(Colors.Transparent);
        }
        else if (isFocused)
        {
            _focusBorder.BorderBrush = FocusBorderBrush;
            _focusBorder.Opacity = 1;
            _mainBorder.BorderBrush = new SolidColorBrush(Colors.Transparent);
        }
        else
        {
            _focusBorder.Opacity = 0;
            _mainBorder.BorderBrush = MainBorderBrush;
        }
    }

    private void FindControls()
    {
        _mainTextBox = this.FindControl<TextBox>("MainTextBox");
        _focusBorder = this.FindControl<Border>("FocusBorder");
        _mainBorder = this.FindControl<Border>("MainBorder");
        _passwordMaskOverlay = this.FindControl<TextBlock>("PasswordMaskOverlay");
    }

    private void OnGotFocus(object? sender, Avalonia.Input.GotFocusEventArgs e) => UpdateBorderState();
    private void OnLostFocus(object? sender, RoutedEventArgs e) => UpdateBorderState();

    private void SetupReactiveBindings()
    {
        this.WhenAnyValue(x => x.HasError)
            .Subscribe(_ => UpdateBorderState())
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.Text)
            .Subscribe(text =>
            {
                if (!IsPasswordMode && _mainTextBox != null && _mainTextBox.Text != text)
                {
                    UpdateTextBox(text, text?.Length ?? 0);
                }
            })
            .DisposeWith(_disposables);
    }

    private void ValidateInput(string input)
    {
        if (_isDisposed || string.IsNullOrEmpty(input))
        {
            HasError = false;
            ErrorText = string.Empty;
            EllipseOpacity = 0;
            return;
        }

        string validationMessage = MembershipValidation.Validate(ValidationType, input);
        if (!string.IsNullOrEmpty(validationMessage))
        {
            ErrorText = validationMessage;
            HasError = true;
            EllipseOpacity = 1.0;
        }
        else
        {
            ErrorText = string.Empty;
            HasError = false;
            EllipseOpacity = 0.0;
        }
    }

    private void UpdateRemainingCharacters()
    {
        if (IsPasswordMode)
        {
            RemainingCharacters = MaxLength - _shadowText.Length;
        }
        else
        {
            RemainingCharacters = MaxLength - (Text?.Length ?? 0);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Dispose();
        base.OnDetachedFromVisualTree(e);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        AttachedToVisualTree -= OnAttachedToVisualTree;
        _disposables.Dispose();
        _mainTextBox = null;
    }
}