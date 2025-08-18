using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.Membership;
using ReactiveUI;

namespace Ecliptix.Core.Controls;

public sealed partial class HintedTextBox : UserControl, IDisposable
{
    public static readonly StyledProperty<bool> IsPasswordModeProperty =
        AvaloniaProperty.Register<HintedTextBox, bool>(nameof(IsSecureKeyMode));

    public static readonly StyledProperty<char> PasswordMaskCharProperty =
        AvaloniaProperty.Register<HintedTextBox, char>(nameof(SecureKeyMaskChar), '●');

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
    
    public static readonly StyledProperty<bool> IsPasswordStrengthModeProperty =
        AvaloniaProperty.Register<HintedTextBox, bool>(nameof(IsPasswordStrengthMode));

    public static readonly StyledProperty<PasswordStrength> PasswordStrengthProperty =
        AvaloniaProperty.Register<HintedTextBox, PasswordStrength>(nameof(PasswordStrength), PasswordStrength.Invalid);

    public static readonly StyledProperty<string> PasswordStrengthTextProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(PasswordStrengthText), string.Empty);

    public static readonly StyledProperty<IBrush> PasswordStrengthTextBrushProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(nameof(PasswordStrengthTextBrush), new SolidColorBrush(Colors.Gray));

    public static readonly StyledProperty<IBrush> PasswordStrengthIconBrushProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(nameof(PasswordStrengthIconBrush), new SolidColorBrush(Colors.Gray));

    public static readonly RoutedEvent<SecureKeyCharactersAddedEventArgs> SecureKeyCharactersAddedEvent =
        RoutedEvent.Register<HintedTextBox, SecureKeyCharactersAddedEventArgs>(nameof(SecureKeyCharactersAdded),
            RoutingStrategies.Bubble);

    public static readonly RoutedEvent<SecureKeyCharactersRemovedEventArgs> SecureKeyCharactersRemovedEvent =
        RoutedEvent.Register<HintedTextBox, SecureKeyCharactersRemovedEventArgs>(nameof(SecureKeyCharactersRemoved),
            RoutingStrategies.Bubble);

    public event EventHandler<SecureKeyCharactersAddedEventArgs> SecureKeyCharactersAdded
    {
        add => AddHandler(SecureKeyCharactersAddedEvent, value);
        remove => RemoveHandler(SecureKeyCharactersAddedEvent, value);
    }

    public event EventHandler<SecureKeyCharactersRemovedEventArgs> SecureKeyCharactersRemoved
    {
        add => AddHandler(SecureKeyCharactersRemovedEvent, value);
        remove => RemoveHandler(SecureKeyCharactersRemovedEvent, value);
    }

    public bool IsSecureKeyMode
    {
        get => GetValue(IsPasswordModeProperty);
        set => SetValue(IsPasswordModeProperty, value);
    }

    public char SecureKeyMaskChar
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
        private set
        {
            _originalErrorText = value;
            SetValue(ErrorTextProperty, value);
        }
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
    
    public bool IsPasswordStrengthMode
    {
        get => GetValue(IsPasswordStrengthModeProperty);
        set => SetValue(IsPasswordStrengthModeProperty, value);
    }

    public PasswordStrength PasswordStrength
    {
        get => GetValue(PasswordStrengthProperty);
        set => SetValue(PasswordStrengthProperty, value);
    }

    public string PasswordStrengthText
    {
        get => GetValue(PasswordStrengthTextProperty);
        set => SetValue(PasswordStrengthTextProperty, value);
    }

    public IBrush PasswordStrengthTextBrush
    {
        get => GetValue(PasswordStrengthTextBrushProperty);
        set => SetValue(PasswordStrengthTextBrushProperty, value);
    }

    public IBrush PasswordStrengthIconBrush
    {
        get => GetValue(PasswordStrengthIconBrushProperty);
        set => SetValue(PasswordStrengthIconBrushProperty, value);
    }

    private readonly CompositeDisposable _disposables = new();
    private TextBox? _mainTextBox;
    private TextBlock? _secureKeyMaskOverlay;
    private Border? _focusBorder;
    private Border? _mainBorder;
    private Border? _shadowBorder;
    private string _shadowText = string.Empty;
    private bool _isUpdatingFromCode;
    private bool _isDisposed;
    private bool _isControlInitialized;
    private int _nextCaretPosition;
    private string _originalErrorText = string.Empty;

    public HintedTextBox()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    public void SyncSecureKeyState(int newPasswordLength)
    {
        _shadowText = newPasswordLength > 0 ? new string(SecureKeyMaskChar, newPasswordLength) : string.Empty;
        UpdateTextBox(_shadowText, Math.Min(_nextCaretPosition, _shadowText.Length));
        UpdateSecureKeyMaskOverlay(newPasswordLength);
        UpdateRemainingCharacters();
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

        if (IsSecureKeyMode)
        {
            string newText = _mainTextBox.Text ?? string.Empty;
            if (newText == _shadowText) return;

            if (string.IsNullOrEmpty(newText) && !string.IsNullOrEmpty(_shadowText))
            {
                int oldLength = _shadowText.Length;
                _nextCaretPosition = 0;
                _shadowText = string.Empty;
                RaiseEvent(new SecureKeyCharactersRemovedEventArgs(SecureKeyCharactersRemovedEvent, 0, oldLength));
                UpdateTextBox(_shadowText, 0);
                UpdateSecureKeyMaskOverlay(0);
                UpdateRemainingCharacters();
                return;
            }

            if (string.IsNullOrEmpty(_shadowText) && !string.IsNullOrEmpty(newText))
            {
                _nextCaretPosition = newText.Length;
                RaiseEvent(new SecureKeyCharactersAddedEventArgs(SecureKeyCharactersAddedEvent, 0, newText));
                return;
            }

            (int diffIndex, int removedCount, string added) = Diff(_shadowText, newText);

            if (removedCount > 0 && string.IsNullOrEmpty(added))
            {
                diffIndex = Math.Clamp(_mainTextBox.CaretIndex, 0, _shadowText.Length);
            }

            _nextCaretPosition = diffIndex + added.Length;

            if (removedCount > 0)
            {
                RaiseEvent(new SecureKeyCharactersRemovedEventArgs(SecureKeyCharactersRemovedEvent, diffIndex,
                    removedCount));
            }

            if (!string.IsNullOrEmpty(added))
            {
                RaiseEvent(new SecureKeyCharactersAddedEventArgs(SecureKeyCharactersAddedEvent, diffIndex, added));
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
    }

    private static (int Index, int RemovedCount, string Added) Diff(string oldStr, string newStr)
    {
        if (string.IsNullOrEmpty(newStr))
        {
            return (0, oldStr.Length, string.Empty);
        }

        if (string.IsNullOrEmpty(oldStr))
        {
            return (0, 0, newStr);
        }

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

    private void UpdateSecureKeyMaskOverlay(int length)
    {
        if (_secureKeyMaskOverlay != null)
        {
            _secureKeyMaskOverlay.Text = length > 0 ? new string(SecureKeyMaskChar, length) : string.Empty;
        }
    }

    private void UpdateBorderState()
    {
        if (_mainTextBox == null || _focusBorder == null || _mainBorder == null || _shadowBorder == null) return;

        bool isFocused = _mainTextBox.IsFocused;

        if (IsPasswordStrengthMode)
        {
            // Password strength mode - use strength-based colors
            (Color borderColor, string shadowKey, Color iconColor) = GetPasswordStrengthColors(PasswordStrength);
            
            _focusBorder.BorderBrush = new SolidColorBrush(borderColor);
            _focusBorder.Opacity = 1;
            _mainBorder.BorderBrush = new SolidColorBrush(Colors.Transparent);
            _shadowBorder.BoxShadow = (BoxShadows)this.FindResource(shadowKey);
            
            // Update strength colors
            PasswordStrengthIconBrush = new SolidColorBrush(iconColor);
            PasswordStrengthTextBrush = new SolidColorBrush(iconColor);
        }
        else if (HasError)
        {
            _focusBorder.BorderBrush = new SolidColorBrush(Color.Parse("#de1e31"));
            _focusBorder.Opacity = 1;
            _mainBorder.BorderBrush = new SolidColorBrush(Colors.Transparent);
            _shadowBorder.BoxShadow = (BoxShadows)this.FindResource("ErrorShadow");
        }
        else if (isFocused)
        {
            _focusBorder.BorderBrush = FocusBorderBrush;
            _focusBorder.Opacity = 1;
            _mainBorder.BorderBrush = new SolidColorBrush(Colors.Transparent);
            _shadowBorder.BoxShadow = (BoxShadows)this.FindResource("FocusShadow");
        }
        else
        {
            _focusBorder.Opacity = 0;
            _mainBorder.BorderBrush = MainBorderBrush;
            _shadowBorder.BoxShadow = (BoxShadows)this.FindResource("DefaultShadow");
        }
    }

    private static (Color BorderColor, string ShadowKey, Color IconColor) GetPasswordStrengthColors(PasswordStrength strength)
    {
        return strength switch
        {
            PasswordStrength.Invalid => (Color.Parse("#ef3a3a"), "InvalidStrengthShadow", Color.Parse("#ef3a3a")),
            PasswordStrength.VeryWeak => (Color.Parse("#ff6b35"), "VeryWeakStrengthShadow", Color.Parse("#ff6b35")),
            PasswordStrength.Weak => (Color.Parse("#ffa500"), "WeakStrengthShadow", Color.Parse("#ffa500")),
            PasswordStrength.Good => (Color.Parse("#f4c20d"), "GoodStrengthShadow", Color.Parse("#f4c20d")),
            PasswordStrength.Strong => (Color.Parse("#90ee90"), "StrongStrengthShadow", Color.Parse("#90ee90")),
            PasswordStrength.VeryStrong => (Color.Parse("#32cd32"), "VeryStrongStrengthShadow", Color.Parse("#32cd32")),
            _ => (Color.Parse("#ef3a3a"), "InvalidStrengthShadow", Color.Parse("#ef3a3a"))
        };
    }

    private void FindControls()
    {
        _mainTextBox = this.FindControl<TextBox>("MainTextBox");
        _focusBorder = this.FindControl<Border>("FocusBorder");
        _mainBorder = this.FindControl<Border>("MainBorder");
        _shadowBorder = this.FindControl<Border>("ShadowBorder");
        _secureKeyMaskOverlay = this.FindControl<TextBlock>("PasswordMaskOverlay");
    }

    private void OnGotFocus(object? sender, Avalonia.Input.GotFocusEventArgs e) => UpdateBorderState();
    private void OnLostFocus(object? sender, RoutedEventArgs e) => UpdateBorderState();

    private void SetupReactiveBindings()
    {
        this.WhenAnyValue(x => x.HasError)
            .Subscribe(_ => UpdateBorderState())
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.PasswordStrength)
            .Subscribe(_ => UpdateBorderState())
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.IsPasswordStrengthMode)
            .Subscribe(_ => UpdateBorderState())
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.Text)
            .Subscribe(text =>
            {
                if (!IsSecureKeyMode && _mainTextBox != null && _mainTextBox.Text != text)
                {
                    UpdateTextBox(text, text?.Length ?? 0);
                }
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.ErrorText)
            .Scan(string.Empty, (previous, current) =>
            {
                return string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(previous) ? previous : current;
            })
            .Subscribe(accumulatedError =>
            {
                SetValue(ErrorTextProperty, accumulatedError);
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.HasError)
            .Subscribe(hasError => { EllipseOpacity = hasError ? 1.0 : 0.0; })
            .DisposeWith(_disposables);
    }

    private void UpdateRemainingCharacters()
    {
        if (IsSecureKeyMode)
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