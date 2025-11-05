using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Ecliptix.Core.Controls.Common;
using Ecliptix.Core.Controls.Constants;
using Ecliptix.Core.Controls.EventArgs;
using Ecliptix.Core.Services.Membership;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Core;

public sealed partial class HintedTextBox : UserControl, IDisposable
{
    public static readonly StyledProperty<bool> IsSecureKeyModeProperty =
        AvaloniaProperty.Register<HintedTextBox, bool>(nameof(IsSecureKeyMode));

    public static readonly StyledProperty<char> SecureKeyMaskCharProperty =
        AvaloniaProperty.Register<HintedTextBox, char>(nameof(SecureKeyMaskChar),
            HintedTextBoxConstants.DEFAULT_MASK_CHAR);

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(Text), string.Empty,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string> WatermarkProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(Watermark), string.Empty);

    public static readonly StyledProperty<string> HintProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(Hint), string.Empty);

    public static readonly StyledProperty<IBrush> FocusBorderBrushProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(
            nameof(FocusBorderBrush), new SolidColorBrush(Color.Parse(HintedTextBoxConstants.FOCUS_COLOR_HEX)));

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
        AvaloniaProperty.Register<HintedTextBox, double>(nameof(FontSize), HintedTextBoxConstants.DEFAULT_FONT_SIZE);

    public static readonly StyledProperty<double> WatermarkFontSizeProperty =
        AvaloniaProperty.Register<HintedTextBox, double>(nameof(WatermarkFontSize),
            HintedTextBoxConstants.DEFAULT_WATERMARK_FONT_SIZE);

    public new static readonly StyledProperty<FontWeight> FontWeightProperty =
        AvaloniaProperty.Register<HintedTextBox, FontWeight>(nameof(FontWeight), FontWeight.Normal);

    public static readonly StyledProperty<bool> IsSecureKeyStrengthModeProperty =
        AvaloniaProperty.Register<HintedTextBox, bool>(nameof(IsSecureKeyStrengthMode));

    public static readonly StyledProperty<SecureKeyStrength> SecureKeyStrengthProperty =
        AvaloniaProperty.Register<HintedTextBox, SecureKeyStrength>(nameof(SecureKeyStrength), SecureKeyStrength.INVALID);

    public static readonly StyledProperty<string> SecureKeyStrengthTextProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(SecureKeyStrengthText), string.Empty);

    public static readonly StyledProperty<IBrush> SecureKeyStrengthTextBrushProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(nameof(SecureKeyStrengthTextBrush),
            new SolidColorBrush(Colors.Gray));

    public static readonly StyledProperty<IBrush> SecureKeyStrengthIconBrushProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(nameof(SecureKeyStrengthIconBrush),
            new SolidColorBrush(Colors.Gray));

    public static readonly StyledProperty<string> WarningTextProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(WarningText), string.Empty);

    public static readonly StyledProperty<bool> HasWarningProperty =
        AvaloniaProperty.Register<HintedTextBox, bool>(nameof(HasWarning));

    public static readonly StyledProperty<int> WarningDisplayDurationMsProperty =
        AvaloniaProperty.Register<HintedTextBox, int>(nameof(WarningDisplayDurationMs),
            HintedTextBoxConstants.DEFAULT_WARNING_DISPLAY_DURATION_MS);

    public static readonly RoutedEvent<CharacterRejectedEventArgs> CharacterRejectedEvent =
        RoutedEvent.Register<HintedTextBox, CharacterRejectedEventArgs>(nameof(CharacterRejected), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<SecureKeyCharactersAddedEventArgs> SecureKeyCharactersAddedEvent =
        RoutedEvent.Register<HintedTextBox, SecureKeyCharactersAddedEventArgs>(nameof(SecureKeyCharactersAdded),
            RoutingStrategies.Bubble);

    public static readonly RoutedEvent<SecureKeyCharactersRemovedEventArgs> SecureKeyCharactersRemovedEvent =
        RoutedEvent.Register<HintedTextBox, SecureKeyCharactersRemovedEventArgs>(nameof(SecureKeyCharactersRemoved),
            RoutingStrategies.Bubble);

    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new();
    private static readonly Dictionary<string, Color> ColorCache = new();
    private static readonly Dictionary<string, BoxShadows> ResourceCache = new();
    private static readonly Dictionary<string, int> TextElementCountCache = new(StringComparer.Ordinal);
    private const int MAX_TEXT_ELEMENT_CACHE_SIZE = 1000;

    private const int INPUT_DEBOUNCE_DELAY_MS = HintedTextBoxConstants.INPUT_DEBOUNCE_DELAY_MS;
    private const int SECURE_KEY_DEBOUNCE_DELAY_MS = 50;

    private readonly CompositeDisposable _disposables = new();

    private TextBox? _mainTextBox;
    private Border? _focusBorder;
    private Border? _mainBorder;
    private Border? _shadowBorder;
    private bool _isUpdatingFromCode;
    private bool _isDisposed;
    private bool _isControlInitialized;
    private DispatcherTimer? _warningTimer;
    private DispatcherTimer? _inputDebounceTimer;
    private DispatcherTimer? _secureKeyDebounceTimer;
    private bool _lastIsFocused;
    private bool _lastHasError;
    private SecureKeyStrength _lastSecureKeyStrength = SecureKeyStrength.INVALID;
    private bool _lastIsSecureKeyStrengthMode;
    private string _lastProcessedText = string.Empty;
    private IDisposable? _currentTypingAnimation;
    private int _lastProcessedTextElementCount;
    private volatile bool _isProcessingSecureKeyChange;
    private int _intendedCaretPosition;

    public HintedTextBox()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    public int WarningDisplayDurationMs
    {
        get => GetValue(WarningDisplayDurationMsProperty);
        set => SetValue(WarningDisplayDurationMsProperty, value);
    }

    public bool IsSecureKeyMode
    {
        get => GetValue(IsSecureKeyModeProperty);
        set
        {
            bool oldValue = GetValue(IsSecureKeyModeProperty);
            SetValue(IsSecureKeyModeProperty, value);
            if (oldValue != value)
            {
                OnIsSecureKeyModeChanged(value);
            }
        }
    }

    public char SecureKeyMaskChar
    {
        get => GetValue(SecureKeyMaskCharProperty);
        set => SetValue(SecureKeyMaskCharProperty, value);
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

    public bool IsSecureKeyStrengthMode
    {
        get => GetValue(IsSecureKeyStrengthModeProperty);
        set => SetValue(IsSecureKeyStrengthModeProperty, value);
    }

    public SecureKeyStrength SecureKeyStrength
    {
        get => GetValue(SecureKeyStrengthProperty);
        set => SetValue(SecureKeyStrengthProperty, value);
    }

    public string SecureKeyStrengthText
    {
        get => GetValue(SecureKeyStrengthTextProperty);
        set => SetValue(SecureKeyStrengthTextProperty, value);
    }

    public IBrush SecureKeyStrengthTextBrush
    {
        get => GetValue(SecureKeyStrengthTextBrushProperty);
        set => SetValue(SecureKeyStrengthTextBrushProperty, value);
    }

    public IBrush SecureKeyStrengthIconBrush
    {
        get => GetValue(SecureKeyStrengthIconBrushProperty);
        set => SetValue(SecureKeyStrengthIconBrushProperty, value);
    }

    public string WarningText
    {
        get => GetValue(WarningTextProperty);
        set => SetValue(WarningTextProperty, value);
    }

    public bool HasWarning
    {
        get => GetValue(HasWarningProperty);
        set => SetValue(HasWarningProperty, value);
    }

    public event EventHandler<CharacterRejectedEventArgs> CharacterRejected
    {
        add => AddHandler(CharacterRejectedEvent, value);
        remove => RemoveHandler(CharacterRejectedEvent, value);
    }

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

    public void SyncSecureKeyState(int newSecureKeyLength)
    {
        if (_mainTextBox == null)
        {
            return;
        }

        string maskText = newSecureKeyLength > 0
            ? new string(SecureKeyMaskChar, newSecureKeyLength)
            : string.Empty;

        _mainTextBox.PasswordChar = HintedTextBoxConstants.NO_SECURE_KEY_CHAR;

        int caretPosition = Math.Clamp(_intendedCaretPosition, 0, newSecureKeyLength);
        UpdateTextBox(maskText, caretPosition);
        UpdateRemainingCharacters();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Dispose();
        base.OnDetachedFromVisualTree(e);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            _isDisposed = true;

            if (_inputDebounceTimer != null)
            {
                _inputDebounceTimer.Stop();
                _inputDebounceTimer.Tick -= OnDebounceTimerTick;
                _inputDebounceTimer = null;
            }

            if (_secureKeyDebounceTimer != null)
            {
                _secureKeyDebounceTimer.Stop();
                _secureKeyDebounceTimer.Tick -= OnSecureKeyDebounceTimerTick;
                _secureKeyDebounceTimer = null;
            }

            if (_warningTimer != null)
            {
                _warningTimer.Stop();
                _warningTimer.Tick -= OnWarningTimerTick;
                _warningTimer = null;
            }

            _currentTypingAnimation?.Dispose();

            AttachedToVisualTree -= OnAttachedToVisualTree;

            try
            {
                _disposables.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR disposing subscriptions: {ex.Message}");
            }

            _mainTextBox = null;
            _focusBorder = null;
            _mainBorder = null;
            _shadowBorder = null;

            _lastProcessedText = string.Empty;
            _lastProcessedTextElementCount = 0;
            _isProcessingSecureKeyChange = false;
            _intendedCaretPosition = 0;

            ErrorText = string.Empty;
            HasError = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR in Dispose: {ex.Message}");
        }
    }

    private static CharacterWarningType GetWarningType(char c)
    {
        if (char.IsLetter(c) && !((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')))
        {
            return CharacterWarningType.NonLatinLetter;
        }

        return CharacterWarningType.InvalidCharacter;
    }

    private static bool IsAllowedCharacter(char c)
    {
        if (char.IsDigit(c))
        {
            return true;
        }

        if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
        {
            return true;
        }

        if (!char.IsLetter(c) && !char.IsDigit(c))
        {
            return true;
        }

        return false;
    }

    private static (Color BorderColor, string ShadowKey, Color IconColor) GetSecureKeyStrengthColors(
        SecureKeyStrength strength)
    {
        return strength switch
        {
            SecureKeyStrength.INVALID => (GetCachedColor(HintedTextBoxConstants.INVALID_STRENGTH_COLOR_HEX),
                HintedTextBoxConstants.INVALID_STRENGTH_SHADOW_KEY,
                GetCachedColor(HintedTextBoxConstants.INVALID_STRENGTH_COLOR_HEX)),
            SecureKeyStrength.VERY_WEAK => (GetCachedColor(HintedTextBoxConstants.VERY_WEAK_STRENGTH_COLOR_HEX),
                HintedTextBoxConstants.VERY_WEAK_STRENGTH_SHADOW_KEY,
                GetCachedColor(HintedTextBoxConstants.VERY_WEAK_STRENGTH_COLOR_HEX)),
            SecureKeyStrength.WEAK => (GetCachedColor(HintedTextBoxConstants.WEAK_STRENGTH_COLOR_HEX),
                HintedTextBoxConstants.WEAK_STRENGTH_SHADOW_KEY,
                GetCachedColor(HintedTextBoxConstants.WEAK_STRENGTH_COLOR_HEX)),
            SecureKeyStrength.GOOD => (GetCachedColor(HintedTextBoxConstants.GOOD_STRENGTH_COLOR_HEX),
                HintedTextBoxConstants.GOOD_STRENGTH_SHADOW_KEY,
                GetCachedColor(HintedTextBoxConstants.GOOD_STRENGTH_COLOR_HEX)),
            SecureKeyStrength.STRONG => (GetCachedColor(HintedTextBoxConstants.STRONG_STRENGTH_COLOR_HEX),
                HintedTextBoxConstants.STRONG_STRENGTH_SHADOW_KEY,
                GetCachedColor(HintedTextBoxConstants.STRONG_STRENGTH_COLOR_HEX)),
            SecureKeyStrength.VERY_STRONG => (GetCachedColor(HintedTextBoxConstants.VERY_STRONG_STRENGTH_COLOR_HEX),
                HintedTextBoxConstants.VERY_STRONG_STRENGTH_SHADOW_KEY,
                GetCachedColor(HintedTextBoxConstants.VERY_STRONG_STRENGTH_COLOR_HEX)),
            _ => (GetCachedColor(HintedTextBoxConstants.INVALID_STRENGTH_COLOR_HEX),
                HintedTextBoxConstants.INVALID_STRENGTH_SHADOW_KEY,
                GetCachedColor(HintedTextBoxConstants.INVALID_STRENGTH_COLOR_HEX))
        };
    }

    private static int GetTextElementCount(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        if (TextElementCountCache.TryGetValue(text, out int cachedCount))
        {
            return cachedCount;
        }

        try
        {
            StringInfo stringInfo = new(text);
            int count = stringInfo.LengthInTextElements;

            switch (TextElementCountCache.Count)
            {
                case < MAX_TEXT_ELEMENT_CACHE_SIZE:
                    TextElementCountCache[text] = count;
                    break;
                case > MAX_TEXT_ELEMENT_CACHE_SIZE:
                    TextElementCountCache.Clear();
                    TextElementCountCache[text] = count;
                    break;
            }

            return count;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HINTED-TEXTBOX] Failed to get text element count, using fallback: {ex.Message}");
            int fallbackCount = text.Length;
            if (TextElementCountCache.Count < MAX_TEXT_ELEMENT_CACHE_SIZE)
            {
                TextElementCountCache[text] = fallbackCount;
            }

            return fallbackCount;
        }
    }

    private static string SafeSubstring(string text, int startIndex, int length)
    {
        if (string.IsNullOrEmpty(text) || startIndex < 0)
        {
            return string.Empty;
        }

        try
        {
            StringInfo stringInfo = new(text);
            int textElementCount = stringInfo.LengthInTextElements;

            if (startIndex >= textElementCount)
            {
                return string.Empty;
            }

            int actualLength = Math.Min(length, textElementCount - startIndex);
            return actualLength <= 0 ? string.Empty : stringInfo.SubstringByTextElements(startIndex, actualLength);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HINTED-TEXTBOX] Failed to substring by text elements, using character fallback: {ex.Message}");
            try
            {
                int safeStart = Math.Min(startIndex, text.Length);
                int safeLength = Math.Min(length, text.Length - safeStart);
                return safeLength > 0 ? text.Substring(safeStart, safeLength) : string.Empty;
            }
            catch (Exception ex2)
            {
                System.Diagnostics.Debug.WriteLine($"[HINTED-TEXTBOX] Failed to substring, returning empty: {ex2.Message}");
                return string.Empty;
            }
        }
    }

    private static string GetAddedTextElements(string? currentText, string lastText, int caretIndex)
    {
        if (string.IsNullOrEmpty(currentText) || string.IsNullOrEmpty(lastText))
        {
            return currentText ?? string.Empty;
        }

        try
        {
            StringInfo currentInfo = new(currentText);
            StringInfo lastInfo = new(lastText);

            int currentCount = currentInfo.LengthInTextElements;
            int lastCount = lastInfo.LengthInTextElements;

            if (currentCount <= lastCount)
            {
                return string.Empty;
            }

            int addedCount = currentCount - lastCount;
            int insertPos = Math.Max(0, Math.Min(caretIndex - addedCount, currentCount - addedCount));

            return SafeSubstring(currentText, insertPos, addedCount);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HINTED-TEXTBOX] Failed to get added text elements, using character fallback: {ex.Message}");
            int addedCount = currentText.Length - lastText.Length;
            if (addedCount <= 0)
            {
                return string.Empty;
            }

            int insertPos = Math.Max(0, caretIndex - addedCount);
            return SafeSubstring(currentText, insertPos, addedCount);
        }
    }

    private static Color GetCachedColor(string colorHex)
    {
        if (ColorCache.TryGetValue(colorHex, out Color color))
        {
            return color;
        }
        color = Color.Parse(colorHex);
        ColorCache[colorHex] = color;

        return color;
    }

    private static SolidColorBrush GetCachedBrush(Color color)
    {
        string key = color.ToString();
        if (BrushCache.TryGetValue(key, out SolidColorBrush? brush))
        {
            return brush;
        }
        brush = new SolidColorBrush(color);
        BrushCache[key] = brush;

        return brush;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_isControlInitialized || _isDisposed)
        {
            return;
        }
        Initialize();
    }

    private void Initialize()
    {
        if (_isControlInitialized)
        {
            return;
        }

        FindControls();
        if (_mainTextBox == null)
        {
            return;
        }

        _mainTextBox.TextChanged += OnTextChanged;
        _mainTextBox.GotFocus += OnGotFocus;
        _mainTextBox.LostFocus += OnLostFocus;

        if (IsSecureKeyMode)
        {
            DisableClipboardOperations();
        }

        _disposables.Add(Disposable.Create(UnsubscribeTextBoxEvents));

        SetupReactiveBindings();
        UpdateBorderState();
        _isControlInitialized = true;
    }

    private void DisableClipboardOperations()
    {
        if (_mainTextBox == null)
        {
            return;
        }

        _mainTextBox.AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        _mainTextBox.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        _mainTextBox.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _mainTextBox.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        _mainTextBox.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);

        _mainTextBox.AddHandler(DragDrop.DragEnterEvent, OnDragEnter, RoutingStrategies.Tunnel);
        _mainTextBox.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Tunnel);
        _mainTextBox.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Tunnel);

        _mainTextBox.AddHandler(Gestures.TappedEvent, OnTapped, RoutingStrategies.Tunnel);
        _mainTextBox.AddHandler(Gestures.DoubleTappedEvent, OnDoubleTapped, RoutingStrategies.Tunnel);
        _mainTextBox.AddHandler(Gestures.HoldingEvent, OnHolding, RoutingStrategies.Tunnel);

        _mainTextBox.SelectionStart = 0;
        _mainTextBox.SelectionEnd = 0;

        _mainTextBox.IsReadOnly = false;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsSecureKeyMode)
        {
            return;
        }
        e.Handled = true;
        if (_mainTextBox != null)
        {
            _mainTextBox.CaretIndex = _mainTextBox.Text?.Length ?? 0;
            _intendedCaretPosition = _mainTextBox.Text?.Length ?? 0;
        }
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsSecureKeyMode || _mainTextBox == null)
        {
            return;
        }
        e.Handled = true;

        _mainTextBox.SelectionStart = 0;
        _mainTextBox.SelectionEnd = 0;
        _mainTextBox.CaretIndex = _mainTextBox.Text?.Length ?? 0;

        if (!_mainTextBox.IsFocused)
        {
            _mainTextBox.Focus();
        }
    }


    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (!IsSecureKeyMode)
        {
            return;
        }
        e.Handled = true;
        if (_mainTextBox != null)
        {
            _mainTextBox.CaretIndex = _mainTextBox.Text?.Length ?? 0;
        }
    }

    private void OnHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (!IsSecureKeyMode)
        {
            return;
        }
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!IsSecureKeyMode || _mainTextBox == null)
        {
            return;
        }
        e.Handled = true;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (!IsSecureKeyMode)
        {
            return;
        }
        e.Handled = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!IsSecureKeyMode)
        {
            return;
        }
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!IsSecureKeyMode)
        {
            return;
        }
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsSecureKeyMode || _mainTextBox == null)
        {
            return;
        }

        e.Handled = true;

        if (!_mainTextBox.IsFocused)
        {
            _mainTextBox.Focus();
        }

        _mainTextBox.CaretIndex = _mainTextBox.Text?.Length ?? 0;
        _intendedCaretPosition = _mainTextBox.Text?.Length ?? 0;
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (!IsSecureKeyMode)
        {
            return;
        }

        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        if (e.Text.Length > 1)
        {
            e.Handled = true;
            CharacterRejectedEventArgs multiCharArgs =
                new CharacterRejectedEventArgs('\0', CharacterWarningType.MultipleCharacters, e.Text)
                {
                    RoutedEvent = CharacterRejectedEvent
                };
            RaiseEvent(multiCharArgs);
            StartWarningTimer();

            if (_mainTextBox != null)
            {
                _mainTextBox.CaretIndex = _mainTextBox.Text?.Length ?? 0;
            }
            return;
        }

        char inputChar = e.Text[0];
        if (!IsAllowedCharacter(inputChar))
        {
            e.Handled = true;
            CharacterWarningType warningType = GetWarningType(inputChar);
            CharacterRejectedEventArgs args = new CharacterRejectedEventArgs(inputChar, warningType)
            {
                RoutedEvent = CharacterRejectedEvent
            };
            RaiseEvent(args);
            StartWarningTimer();

            if (_mainTextBox != null)
            {
                _mainTextBox.CaretIndex = _mainTextBox.Text?.Length ?? 0;
            }
        }

        if (_mainTextBox != null)
        {
            int currentLength = _mainTextBox.Text?.Length ?? 0;
            _mainTextBox.CaretIndex = currentLength;
        }
    }

    private void StartWarningTimer()
    {
        if (_warningTimer == null)
        {
            _warningTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(WarningDisplayDurationMs)
            };
            _warningTimer.Tick += OnWarningTimerTick;
        }
        else
        {
            _warningTimer.Interval = TimeSpan.FromMilliseconds(WarningDisplayDurationMs);
        }

        _warningTimer.Stop();
        _warningTimer.Start();
    }

    private void OnWarningTimerTick(object? sender, System.EventArgs e)
    {
        HasWarning = false;
        _warningTimer?.Stop();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsSecureKeyMode)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            switch (e.Key)
            {
                case Key.V:
                case Key.C:
                case Key.X:
                case Key.Z:
                case Key.Y:
                    e.Handled = true;
                    return;
            }
        }
        if (e.Key == Key.Insert && (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            e.Handled = true;
        }

        if (_mainTextBox != null)
        {
            switch (e.Key)
            {
                case Key.Back:
                case Key.Delete:
                case Key.End:
                case Key.Home:
                    return;

                case Key.Left:
                case Key.Right:
                case Key.Up:
                case Key.Down:
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.Back)
        {
            e.Handled = true;
            if (_mainTextBox != null && !string.IsNullOrEmpty(_mainTextBox.Text))
            {
                string currentText = _mainTextBox.Text;
                string newText = currentText.Length > 0 ? currentText.Substring(0, currentText.Length - 1) : string.Empty;

                _isUpdatingFromCode = true;
                _mainTextBox.Text = newText;
                _mainTextBox.CaretIndex = newText.Length;
                _isUpdatingFromCode = false;

                _intendedCaretPosition = newText.Length;
            }
            return;
        }

        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            if (_mainTextBox != null && !string.IsNullOrEmpty(_mainTextBox.Text))
            {
                string currentText = _mainTextBox.Text;
                string newText = currentText.Length > 0 ? currentText.Substring(0, currentText.Length - 1) : string.Empty;

                _isUpdatingFromCode = true;
                _mainTextBox.Text = newText;
                _mainTextBox.CaretIndex = newText.Length;
                _isUpdatingFromCode = false;

                _intendedCaretPosition = newText.Length;
            }
            return;
        }

        if (_mainTextBox != null)
        {
            _mainTextBox.CaretIndex = _mainTextBox.Text?.Length ?? 0;
        }
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingFromCode || _mainTextBox == null || _isDisposed)
        {
            return;
        }

        if (IsSecureKeyMode)
        {
            DebouncedProcessSecureKeyChange();
        }
        else
        {
            DebouncedProcessTextChange();
        }
    }

    private void DebouncedProcessTextChange()
    {
        if (_inputDebounceTimer != null)
        {
            _inputDebounceTimer.Stop();
        }

        if (_inputDebounceTimer == null)
        {
            _inputDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(INPUT_DEBOUNCE_DELAY_MS)
            };
            _inputDebounceTimer.Tick += OnDebounceTimerTick;
        }

        _inputDebounceTimer.Start();
    }

    private void DebouncedProcessSecureKeyChange()
    {
        _secureKeyDebounceTimer?.Stop();

        if (_secureKeyDebounceTimer == null)
        {
            _secureKeyDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(SECURE_KEY_DEBOUNCE_DELAY_MS)
            };
            _secureKeyDebounceTimer.Tick += OnSecureKeyDebounceTimerTick;
        }

        _secureKeyDebounceTimer.Start();
    }

    private void OnSecureKeyDebounceTimerTick(object? sender, System.EventArgs e)
    {
        try
        {
            _secureKeyDebounceTimer?.Stop();

            if (!_isDisposed)
            {
                ProcessSecureKeyChange();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR in OnSecureKeyDebounceTimerTick: {ex.Message}");
        }
    }

    private void OnDebounceTimerTick(object? sender, System.EventArgs e)
    {
        try
        {
            _inputDebounceTimer?.Stop();

            if (!_isDisposed)
            {
                ProcessTextChange();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR in OnDebounceTimerTick: {ex.Message}");
        }
    }

    private void ProcessSecureKeyChange()
    {
        if (_isUpdatingFromCode || _mainTextBox == null || _isDisposed || _isProcessingSecureKeyChange)
        {
            return;
        }

        _isProcessingSecureKeyChange = true;
        try
        {
            string currentText = _mainTextBox.Text ?? string.Empty;
            string lastText = _lastProcessedText;

            if (currentText == lastText)
            {
                return;
            }

            try
            {
                int currentElementCount = GetTextElementCount(currentText);
                int lastElementCount = _lastProcessedTextElementCount > 0
                    ? _lastProcessedTextElementCount
                    : GetTextElementCount(lastText);

                if (currentElementCount > lastElementCount)
                {
                    int addedCount = currentElementCount - lastElementCount;
                    string addedChars = GetAddedTextElements(currentText, lastText, _mainTextBox.CaretIndex);

                    int insertPos = Math.Max(HintedTextBoxConstants.INITIAL_CARET_INDEX, _mainTextBox.CaretIndex - addedCount);

                    _intendedCaretPosition = insertPos + addedCount;

                    if (!string.IsNullOrEmpty(addedChars))
                    {
                        RaiseEvent(new SecureKeyCharactersAddedEventArgs(SecureKeyCharactersAddedEvent, insertPos,
                            addedChars));
                        TriggerTypingAnimation();
                    }
                }
                else if (currentElementCount < lastElementCount)
                {
                    int removedCount = lastElementCount - currentElementCount;
                    int removePos = currentElementCount;

                    _intendedCaretPosition = removePos;

                    RaiseEvent(
                        new SecureKeyCharactersRemovedEventArgs(SecureKeyCharactersRemovedEvent, removePos,
                            removedCount));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in ProcessSecureKeyChange: {ex.Message}");

                if (currentText.Length > lastText.Length)
                {
                    int addedCount = currentText.Length - lastText.Length;
                    int insertPos = Math.Max(HintedTextBoxConstants.INITIAL_CARET_INDEX, _mainTextBox.CaretIndex - addedCount);
                    string addedChars = SafeSubstring(currentText, insertPos, addedCount);

                    _intendedCaretPosition = insertPos + addedCount;

                    if (!string.IsNullOrEmpty(addedChars))
                    {
                        RaiseEvent(new SecureKeyCharactersAddedEventArgs(SecureKeyCharactersAddedEvent, insertPos,
                            addedChars));
                        TriggerTypingAnimation();
                    }
                }
                else if (currentText.Length < lastText.Length)
                {
                    int removedCount = lastText.Length - currentText.Length;
                    int removePos = _mainTextBox.CaretIndex;

                    _intendedCaretPosition = removePos;

                    RaiseEvent(
                        new SecureKeyCharactersRemovedEventArgs(SecureKeyCharactersRemovedEvent, removePos,
                            removedCount));
                }
            }

            _lastProcessedText = currentText;
            _lastProcessedTextElementCount = GetTextElementCount(currentText);
            UpdateRemainingCharacters();
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_mainTextBox != null && !_isDisposed)
                {
                    int textLength = _mainTextBox.Text?.Length ?? 0;
                    if (_mainTextBox.CaretIndex != textLength)
                    {
                        _mainTextBox.CaretIndex = textLength;
                    }
                }
            });
            _isProcessingSecureKeyChange = false;
        }
    }

    private void ProcessTextChange()
    {
        if (_isUpdatingFromCode || _mainTextBox == null || _isDisposed)
        {
            return;
        }
        ProcessStandardChange();
    }

    private void ProcessStandardChange()
    {
        string input = _mainTextBox!.Text ?? string.Empty;

        if (IsNumericOnly)
        {
            string filtered = string.Concat(input.Where(char.IsDigit));
            if (input != filtered)
            {
                int inputElementCount = GetTextElementCount(input);
                int filteredElementCount = GetTextElementCount(filtered);
                int caret = _mainTextBox.CaretIndex - (inputElementCount - filteredElementCount);
                UpdateTextBox(filtered, Math.Max(0, caret));
                return;
            }
        }

        Text = input;
        UpdateRemainingCharacters();

        if (!IsSecureKeyMode && GetTextElementCount(input) > GetTextElementCount(Text) -
            HintedTextBoxConstants.TYPING_ANIMATION_THRESHOLD)
        {
            TriggerTypingAnimation();
        }
    }

    private void UpdateTextBox(string? text, int caretIndex)
    {
        if (_mainTextBox == null)
        {
            return;
        }

        text ??= string.Empty;
        _isUpdatingFromCode = true;
        _mainTextBox.Text = text;
        _mainTextBox.CaretIndex = Math.Clamp(caretIndex, HintedTextBoxConstants.INITIAL_CARET_INDEX, text.Length);
        _isUpdatingFromCode = false;
    }

    private void UpdateBorderState()
    {
        if (_mainTextBox == null || _focusBorder == null || _mainBorder == null || _shadowBorder == null)
        {
            return;
        }

        bool currentHasError = HasError;
        SecureKeyStrength currentSecureKeyStrength = SecureKeyStrength;
        bool currentIsSecureKeyStrengthMode = IsSecureKeyStrengthMode;

        Dispatcher.UIThread.Post(() =>
        {
            if (_isDisposed)
            {
                return;
            }
            if (_mainTextBox == null || _focusBorder == null || _mainBorder == null || _shadowBorder == null)
            {
                return;
            }

            bool isFocused = _mainTextBox.IsFocused;
            if (isFocused == _lastIsFocused
                && currentHasError == _lastHasError
                && currentSecureKeyStrength == _lastSecureKeyStrength
                && currentIsSecureKeyStrengthMode == _lastIsSecureKeyStrengthMode)
            {
                return;
            }

            _lastIsFocused = isFocused;
            _lastHasError = currentHasError;
            _lastSecureKeyStrength = currentSecureKeyStrength;
            _lastIsSecureKeyStrengthMode = currentIsSecureKeyStrengthMode;

            UpdateBorderStateInternal(isFocused, currentHasError, currentSecureKeyStrength, currentIsSecureKeyStrengthMode);
        }, DispatcherPriority.Render);
    }

    private void UpdateBorderStateInternal(bool isFocused, bool hasError, SecureKeyStrength secureKeyStrength, bool isSecureKeyStrengthMode)
    {
        if (_focusBorder == null || _mainBorder == null || _shadowBorder == null)
        {
            return;
        }

        if (isSecureKeyStrengthMode)
        {
            (Color borderColor, string shadowKey, Color iconColor) = GetSecureKeyStrengthColors(secureKeyStrength);

            _focusBorder.BorderBrush = GetCachedBrush(borderColor);
            _focusBorder.Opacity = HintedTextBoxConstants.FULL_OPACITY;
            _mainBorder.BorderBrush = GetCachedBrush(Colors.Transparent);
            _shadowBorder.BoxShadow = GetCachedResource(shadowKey);

            SecureKeyStrengthIconBrush = GetCachedBrush(iconColor);
            SecureKeyStrengthTextBrush = GetCachedBrush(iconColor);
        }
        else if (hasError)
        {
            _focusBorder.BorderBrush = GetCachedBrush(GetCachedColor(HintedTextBoxConstants.ERROR_COLOR_HEX));
            _focusBorder.Opacity = HintedTextBoxConstants.FULL_OPACITY;
            _mainBorder.BorderBrush = GetCachedBrush(Colors.Transparent);
            _shadowBorder.BoxShadow = GetCachedResource(HintedTextBoxConstants.ERROR_SHADOW_KEY);
        }
        else if (isFocused)
        {
            _focusBorder.BorderBrush = FocusBorderBrush;
            _focusBorder.Opacity = HintedTextBoxConstants.FULL_OPACITY;
            _mainBorder.BorderBrush = GetCachedBrush(Colors.Transparent);
            _shadowBorder.BoxShadow = GetCachedResource(HintedTextBoxConstants.FOCUS_SHADOW_KEY);
        }
        else
        {
            _focusBorder.Opacity = HintedTextBoxConstants.ZERO_OPACITY;
            _mainBorder.BorderBrush = MainBorderBrush;
            _shadowBorder.BoxShadow = GetCachedResource(HintedTextBoxConstants.DEFAULT_SHADOW_KEY);
        }
    }

    private void UnsubscribeTextBoxEvents()
    {
        if (_mainTextBox == null)
        {
            return;
        }
        _mainTextBox.TextChanged -= OnTextChanged;
        _mainTextBox.GotFocus -= OnGotFocus;
        _mainTextBox.LostFocus -= OnLostFocus;
        _mainTextBox.RemoveHandler(TextInputEvent, OnTextInput);
        _mainTextBox.RemoveHandler(KeyDownEvent, OnPreviewKeyDown);
        _mainTextBox.RemoveHandler(PointerPressedEvent, OnPointerPressed);
        _mainTextBox.RemoveHandler(PointerMovedEvent, OnPointerMoved);
        _mainTextBox.RemoveHandler(PointerReleasedEvent, OnPointerReleased);
        _mainTextBox.RemoveHandler(DragDrop.DragEnterEvent, OnDragEnter);
        _mainTextBox.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
        _mainTextBox.RemoveHandler(DragDrop.DropEvent, OnDrop);
        _mainTextBox.RemoveHandler(Gestures.TappedEvent, OnTapped);
        _mainTextBox.RemoveHandler(Gestures.DoubleTappedEvent, OnDoubleTapped);
        _mainTextBox.RemoveHandler(Gestures.HoldingEvent, OnHolding);
    }

    private void FindControls()
    {
        _mainTextBox = this.FindControl<TextBox>(HintedTextBoxConstants.MAIN_TEXT_BOX_NAME);
        _focusBorder = this.FindControl<Border>(HintedTextBoxConstants.FOCUS_BORDER_NAME);
        _mainBorder = this.FindControl<Border>(HintedTextBoxConstants.MAIN_BORDER_NAME);
        _shadowBorder = this.FindControl<Border>(HintedTextBoxConstants.SHADOW_BORDER_NAME);
    }

    private void OnGotFocus(object? sender, GotFocusEventArgs e)
    {
        try
        {
            if (!_isDisposed)
            {
                UpdateBorderState();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR in OnGotFocus: {ex.Message}");
        }
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!_isDisposed)
            {
                UpdateBorderState();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR in OnLostFocus: {ex.Message}");
        }
    }

    private void SetupReactiveBindings()
    {
        this.WhenAnyValue(
                x => x.HasError,
                x => x.SecureKeyStrength,
                x => x.IsSecureKeyStrengthMode,
                (hasError, secureKeyStrength, isSecureKeyStrengthMode) => (hasError, secureKeyStrength, isSecureKeyStrengthMode))
            .DistinctUntilChanged()
            .Subscribe(_ =>
            {
                try
                {
                    if (!_isDisposed)
                    {
                        UpdateBorderState();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR in UpdateBorderState subscription: {ex.Message}");
                }
            })
            .DisposeWith(_disposables);


        this.WhenAnyValue(x => x.ErrorText)
            .DistinctUntilChanged()
            .Scan(string.Empty, (previous, current) =>
                string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(previous) ? previous : current)
            .Subscribe(accumulatedError =>
            {
                try
                {
                    if (!_isDisposed && !string.IsNullOrEmpty(accumulatedError))
                    {
                        SetValue(ErrorTextProperty, accumulatedError);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR in ErrorText subscription: {ex.Message}");
                }
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.Text)
            .Where(text => !IsSecureKeyMode && _mainTextBox != null && _mainTextBox.Text != text)
            .Subscribe(text =>
            {
                try
                {
                    if (!_isDisposed)
                    {
                        UpdateTextBox(text, (text).Length);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR in Text subscription: {ex.Message}");
                }
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.HasError)
            .DistinctUntilChanged()
            .Subscribe(hasError =>
            {
                try
                {
                    if (!_isDisposed)
                    {
                        EllipseOpacity = hasError
                            ? HintedTextBoxConstants.DEFAULT_ELLIPSE_OPACITY_VISIBLE
                            : HintedTextBoxConstants.DEFAULT_ELLIPSE_OPACITY_HIDDEN;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR in HasError subscription: {ex.Message}");
                }
            })
            .DisposeWith(_disposables);
    }

    private void UpdateRemainingCharacters()
    {
        int currentTextLength = IsSecureKeyMode && _mainTextBox != null
            ? GetTextElementCount(_mainTextBox.Text ?? string.Empty)
            : GetTextElementCount(Text);
        RemainingCharacters = MaxLength - currentTextLength;
    }

    private void OnIsSecureKeyModeChanged(bool isSecureKeyMode)
    {
        if (_mainTextBox == null)
        {
            return;
        }

        _mainTextBox.PasswordChar = HintedTextBoxConstants.NO_SECURE_KEY_CHAR;

        if (isSecureKeyMode)
        {
            _lastProcessedText = _mainTextBox.Text ?? string.Empty;
            DisableClipboardOperations();
        }
        else
        {
            _lastProcessedText = string.Empty;
            EnableClipboardOperations();
        }

        UpdateRemainingCharacters();
    }

    private void EnableClipboardOperations()
    {
        if (_mainTextBox == null)
        {
            return;
        }

        _mainTextBox.RemoveHandler(TextInputEvent, OnTextInput);
        _mainTextBox.RemoveHandler(KeyDownEvent, OnPreviewKeyDown);
        _mainTextBox.RemoveHandler(PointerPressedEvent, OnPointerPressed);
        _mainTextBox.RemoveHandler(PointerMovedEvent, OnPointerMoved);
        _mainTextBox.RemoveHandler(PointerReleasedEvent, OnPointerReleased);
        _mainTextBox.RemoveHandler(DragDrop.DragEnterEvent, OnDragEnter);
        _mainTextBox.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
        _mainTextBox.RemoveHandler(DragDrop.DropEvent, OnDrop);
        _mainTextBox.RemoveHandler(Gestures.TappedEvent, OnTapped);
        _mainTextBox.RemoveHandler(Gestures.DoubleTappedEvent, OnDoubleTapped);
        _mainTextBox.RemoveHandler(Gestures.HoldingEvent, OnHolding);
    }

    private void TriggerTypingAnimation()
    {
        if (_mainTextBox == null || _isDisposed || _focusBorder == null)
        {
            return;
        }

        try
        {
            _currentTypingAnimation?.Dispose();

            if (_focusBorder.Opacity <= HintedTextBoxConstants.ZERO_OPACITY)
            {
                return;
            }
            Animation pulseAnimation = new()
            {
                Duration = TimeSpan.FromMilliseconds(HintedTextBoxConstants.TYPING_ANIMATION_DURATION_MS),
                FillMode = FillMode.None,
                Easing = new CubicEaseOut()
            };

            KeyFrame startFrame = new()
            {
                Cue = Cue.Parse(HintedTextBoxConstants.ANIMATION_START_PERCENT, CultureInfo.InvariantCulture),
                Setters = { new Setter { Property = OpacityProperty, Value = _focusBorder.Opacity } }
            };

            KeyFrame brightFrame = new()
            {
                Cue = Cue.Parse(HintedTextBoxConstants.ANIMATION_PEAK_PERCENT, CultureInfo.InvariantCulture),
                Setters =
                {
                    new Setter
                    {
                        Property = OpacityProperty,
                        Value = Math.Min(HintedTextBoxConstants.FULL_OPACITY,
                            _focusBorder.Opacity + HintedTextBoxConstants.ANIMATION_OPACITY_BOOST)
                    }
                }
            };

            KeyFrame endFrame = new()
            {
                Cue = Cue.Parse(HintedTextBoxConstants.ANIMATION_END_PERCENT, CultureInfo.InvariantCulture),
                Setters = { new Setter { Property = OpacityProperty, Value = _focusBorder.Opacity } }
            };

            pulseAnimation.Children.Add(startFrame);
            pulseAnimation.Children.Add(brightFrame);
            pulseAnimation.Children.Add(endFrame);

            _currentTypingAnimation = pulseAnimation.RunAsync(_focusBorder);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR in TriggerTypingAnimation: {ex.Message}");
        }
    }

    private BoxShadows GetCachedResource(string resourceKey)
    {
        if (ResourceCache.TryGetValue(resourceKey, out BoxShadows shadow))
        {
            return shadow;
        }
        shadow = this.FindResource(resourceKey) is BoxShadows foundShadow ? foundShadow : default;
        ResourceCache[resourceKey] = shadow;

        return shadow;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
