using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Ecliptix.Core.Controls.Common;
using Ecliptix.Core.Controls.Constants;
using Ecliptix.Core.Controls.EventArgs;
using Ecliptix.Core.Services.Membership;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Core.HintedTextControls;

public sealed partial class HintedPasswordBox : UserControl, IDisposable
{
    #region Constants & Fields

    private const string CLASS_STRENGTH_INVALID = "strength-invalid";
    private const string CLASS_STRENGTH_VERY_WEAK = "strength-very-weak";
    private const string CLASS_STRENGTH_WEAK = "strength-weak";
    private const string CLASS_STRENGTH_GOOD = "strength-good";
    private const string CLASS_STRENGTH_STRONG = "strength-strong";
    private const string CLASS_STRENGTH_VERY_STRONG = "strength-very-strong";

    private static readonly FrozenDictionary<SecureKeyStrength, string> StrengthClassMap =
        new Dictionary<SecureKeyStrength, string>
        {
            [SecureKeyStrength.INVALID] = CLASS_STRENGTH_INVALID,
            [SecureKeyStrength.VERY_WEAK] = CLASS_STRENGTH_VERY_WEAK,
            [SecureKeyStrength.WEAK] = CLASS_STRENGTH_WEAK,
            [SecureKeyStrength.GOOD] = CLASS_STRENGTH_GOOD,
            [SecureKeyStrength.STRONG] = CLASS_STRENGTH_STRONG,
            [SecureKeyStrength.VERY_STRONG] = CLASS_STRENGTH_VERY_STRONG
        }.ToFrozenDictionary();

    private readonly Dictionary<string, int> TextElementCountCache = new(StringComparer.Ordinal);
    private const int MAX_TEXT_ELEMENT_CACHE_SIZE = 1000;
    private const int SECURE_KEY_DEBOUNCE_DELAY_MS = 50;

    private readonly CompositeDisposable _disposables = new();

    private TextBox? _mainTextBox;
    private Border? _focusBorder;
    private Border? _mainBorder;
    private Border? _shadowBorder;
    private Grid? _mainGrid;

    private bool _isUpdatingFromCode;
    private bool _isDisposed;
    private bool _isControlInitialized;

    private DispatcherTimer? _warningTimer;
    private DispatcherTimer? _secureKeyDebounceTimer;

    private string _lastProcessedText = string.Empty;
    private int _lastProcessedTextElementCount;
    private volatile bool _isProcessingSecureKeyChange;
    private int _intendedCaretPosition;
    private string? _currentStrengthClass;

    private bool _showVisualIcon;
    private bool _showLeftSeparator;

    #endregion

    #region Styled Properties

    public static readonly StyledProperty<bool> IsSecureKeyModeProperty =
        AvaloniaProperty.Register<HintedPasswordBox, bool>(nameof(IsSecureKeyMode), true);

    public static readonly StyledProperty<char> SecureKeyMaskCharProperty =
        AvaloniaProperty.Register<HintedPasswordBox, char>(nameof(SecureKeyMaskChar),
            HintedTextBoxConstants.DEFAULT_MASK_CHAR);

    public static readonly StyledProperty<string> WatermarkProperty =
        AvaloniaProperty.Register<HintedPasswordBox, string>(nameof(Watermark), string.Empty);

    public static readonly StyledProperty<string> HintProperty =
        AvaloniaProperty.Register<HintedPasswordBox, string>(nameof(Hint), string.Empty);

    public static readonly StyledProperty<DrawingImage?> IconRegularSourceProperty =
        AvaloniaProperty.Register<HintedPasswordBox, DrawingImage?>(nameof(IconRegularSource));

    public static readonly StyledProperty<DrawingImage?> IconErrorSourceProperty =
        AvaloniaProperty.Register<HintedPasswordBox, DrawingImage?>(nameof(IconErrorSource));

    public static readonly StyledProperty<IconKind> VisualIconProperty =
        AvaloniaProperty.Register<HintedPasswordBox, IconKind>(nameof(VisualIcon));

    public static readonly StyledProperty<IBrush> SecureKeyStrengthTextBrushProperty =
        AvaloniaProperty.Register<HintedPasswordBox, IBrush>(nameof(SecureKeyStrengthTextBrush),
            new SolidColorBrush(Colors.Gray));

    public static readonly StyledProperty<IBrush> SecureKeyStrengthIconBrushProperty =
        AvaloniaProperty.Register<HintedPasswordBox, IBrush>(nameof(SecureKeyStrengthIconBrush),
            new SolidColorBrush(Colors.Gray));

    public static readonly StyledProperty<IBrush> FocusBorderBrushProperty =
        AvaloniaProperty.Register<HintedPasswordBox, IBrush>(
            nameof(FocusBorderBrush), new SolidColorBrush(Color.Parse(HintedTextBoxConstants.FOCUS_COLOR_HEX)));

    public static readonly StyledProperty<IBrush> TextForegroundProperty =
        AvaloniaProperty.Register<HintedPasswordBox, IBrush>(
            nameof(TextForeground), new SolidColorBrush(Colors.Black));

    public static readonly StyledProperty<IBrush> HintForegroundProperty =
        AvaloniaProperty.Register<HintedPasswordBox, IBrush>(
            nameof(HintForeground), new SolidColorBrush(Color.Parse(HintedTextBoxConstants.FOCUS_COLOR_HEX)));

    public static readonly StyledProperty<string> ErrorTextProperty =
        AvaloniaProperty.Register<HintedPasswordBox, string>(nameof(ErrorText), string.Empty);

    public static readonly StyledProperty<double> EllipseOpacityProperty =
        AvaloniaProperty.Register<HintedPasswordBox, double>(nameof(EllipseOpacity));

    public static readonly StyledProperty<bool> HasErrorProperty =
        AvaloniaProperty.Register<HintedPasswordBox, bool>(nameof(HasError));

    public static readonly StyledProperty<IBrush> MainBorderBrushProperty =
        AvaloniaProperty.Register<HintedPasswordBox, IBrush>(
            nameof(MainBorderBrush), new SolidColorBrush(Color.Parse(HintedTextBoxConstants.FOCUS_COLOR_HEX)));

    public static readonly StyledProperty<int> MaxLengthProperty =
        AvaloniaProperty.Register<HintedPasswordBox, int>(nameof(MaxLength), int.MaxValue);

    public static readonly StyledProperty<int> RemainingCharactersProperty =
        AvaloniaProperty.Register<HintedPasswordBox, int>(nameof(RemainingCharacters), int.MaxValue);

    public static readonly StyledProperty<bool> ShowCharacterCounterProperty =
        AvaloniaProperty.Register<HintedPasswordBox, bool>(nameof(ShowCharacterCounter));

    public new static readonly StyledProperty<IBrush> BackgroundProperty =
        AvaloniaProperty.Register<HintedPasswordBox, IBrush>(
            nameof(Background), new SolidColorBrush(Colors.White));

    public new static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<HintedPasswordBox, double>(nameof(FontSize), HintedTextBoxConstants.DEFAULT_FONT_SIZE);

    public static readonly StyledProperty<double> WatermarkFontSizeProperty =
        AvaloniaProperty.Register<HintedPasswordBox, double>(nameof(WatermarkFontSize),
            HintedTextBoxConstants.DEFAULT_WATERMARK_FONT_SIZE);

    public new static readonly StyledProperty<FontWeight> FontWeightProperty =
        AvaloniaProperty.Register<HintedPasswordBox, FontWeight>(nameof(FontWeight), FontWeight.Normal);

    public static readonly StyledProperty<bool> IsSecureKeyStrengthModeProperty =
        AvaloniaProperty.Register<HintedPasswordBox, bool>(nameof(IsSecureKeyStrengthMode));

    public static readonly StyledProperty<SecureKeyStrength> SecureKeyStrengthProperty =
        AvaloniaProperty.Register<HintedPasswordBox, SecureKeyStrength>(nameof(SecureKeyStrength));

    public static readonly StyledProperty<string> SecureKeyStrengthTextProperty =
        AvaloniaProperty.Register<HintedPasswordBox, string>(nameof(SecureKeyStrengthText), string.Empty);

    public static readonly StyledProperty<string> WarningTextProperty =
        AvaloniaProperty.Register<HintedPasswordBox, string>(nameof(WarningText), string.Empty);

    public static readonly StyledProperty<bool> HasWarningProperty =
        AvaloniaProperty.Register<HintedPasswordBox, bool>(nameof(HasWarning));

    public static readonly StyledProperty<int> WarningDisplayDurationMsProperty =
        AvaloniaProperty.Register<HintedPasswordBox, int>(nameof(WarningDisplayDurationMs),
            HintedTextBoxConstants.DEFAULT_WARNING_DISPLAY_DURATION_MS);

    public static readonly StyledProperty<double> VisualIconStrokeThicknessProperty =
        AvaloniaProperty.Register<HintedPasswordBox, double>(
            nameof(VisualIconStrokeThickness),
            defaultValue: 2);

    public static readonly DirectProperty<HintedPasswordBox, bool> ShowVisualIconProperty =
        AvaloniaProperty.RegisterDirect<HintedPasswordBox, bool>(
            nameof(ShowVisualIcon),
            o => o.ShowVisualIcon);

    public static readonly DirectProperty<HintedPasswordBox, bool> ShowLeftSeparatorProperty =
        AvaloniaProperty.RegisterDirect<HintedPasswordBox, bool>(
            nameof(ShowLeftSeparator),
            o => o.ShowLeftSeparator);

    #endregion

    #region Routed Events

    public readonly RoutedEvent<CharacterRejectedEventArgs> CharacterRejectedEvent =
        RoutedEvent.Register<HintedPasswordBox, CharacterRejectedEventArgs>(nameof(CharacterRejected),
            RoutingStrategies.Bubble);

    public readonly RoutedEvent<SecureKeyCharactersAddedEventArgs> SecureKeyCharactersAddedEvent =
        RoutedEvent.Register<HintedPasswordBox, SecureKeyCharactersAddedEventArgs>(nameof(SecureKeyCharactersAdded),
            RoutingStrategies.Bubble);

    public readonly RoutedEvent<SecureKeyCharactersRemovedEventArgs> SecureKeyCharactersRemovedEvent =
        RoutedEvent.Register<HintedPasswordBox, SecureKeyCharactersRemovedEventArgs>(nameof(SecureKeyCharactersRemoved),
            RoutingStrategies.Bubble);

    #endregion

    public HintedPasswordBox()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    #region Accessors

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

    public IconKind VisualIcon
    {
        get => GetValue(VisualIconProperty);
        set => SetValue(VisualIconProperty, value);
    }

    public double VisualIconStrokeThickness
    {
        get => GetValue(VisualIconStrokeThicknessProperty);
        set => SetValue(VisualIconStrokeThicknessProperty, value);
    }

    public bool ShowVisualIcon
    {
        get => _showVisualIcon;
        private set => SetAndRaise(ShowVisualIconProperty, ref _showVisualIcon, value);
    }

    public bool ShowLeftSeparator
    {
        get => _showLeftSeparator;
        private set => SetAndRaise(ShowLeftSeparatorProperty, ref _showLeftSeparator, value);
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

    public int WarningDisplayDurationMs
    {
        get => GetValue(WarningDisplayDurationMsProperty);
        set => SetValue(WarningDisplayDurationMsProperty, value);
    }

    public bool IsSecureKeyMode
    {
        get => GetValue(IsSecureKeyModeProperty);
        set => SetValue(IsSecureKeyModeProperty, value);
    }

    public char SecureKeyMaskChar
    {
        get => GetValue(SecureKeyMaskCharProperty);
        set => SetValue(SecureKeyMaskCharProperty, value);
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

    public string ErrorText
    {
        get => GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }

    public double EllipseOpacity
    {
        get => GetValue(EllipseOpacityProperty);
        private set => SetValue(EllipseOpacityProperty, value);
    }

    public bool HasError
    {
        get => GetValue(HasErrorProperty);
        set => SetValue(HasErrorProperty, value);
    }

    public IBrush MainBorderBrush
    {
        get => GetValue(MainBorderBrushProperty);
        set => SetValue(MainBorderBrushProperty, value);
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

    #endregion

    #region Event Wrappers

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

    #endregion

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

            AttachedToVisualTree -= OnAttachedToVisualTree;

            try
            {
                _disposables.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR disposing subscriptions: {ex.Message}");
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
            Debug.WriteLine($"ERROR in Dispose: {ex.Message}");
        }
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

        DisableClipboardOperations();

        _disposables.Add(Disposable.Create(UnsubscribeTextBoxEvents));

        SetupReactiveBindings();
        UpdateVisualClasses();

        _lastProcessedText = string.Empty;
        _mainTextBox.PasswordChar = HintedTextBoxConstants.NO_SECURE_KEY_CHAR;
        UpdateRemainingCharacters();

        _isControlInitialized = true;
    }

    private void SetupReactiveBindings()
    {
        this.WhenAnyValue(
                x => x.SecureKeyStrength,
                x => x.IsSecureKeyStrengthMode)
            .DistinctUntilChanged()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => UpdateVisualClasses())
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.ErrorText)
            .DistinctUntilChanged()
            .Scan(string.Empty, (previous, current) =>
                string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(previous) ? previous : current)
            .Subscribe(accumulatedError => SafeExecute(() =>
            {
                if (!string.IsNullOrEmpty(accumulatedError))
                {
                    SetValue(ErrorTextProperty, accumulatedError);
                }
            }, "ErrorText subscription"))
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.HasError)
            .DistinctUntilChanged()
            .Subscribe(hasError => SafeExecute(() =>
            {
                EllipseOpacity = hasError
                    ? HintedTextBoxConstants.DEFAULT_ELLIPSE_OPACITY_VISIBLE
                    : HintedTextBoxConstants.DEFAULT_ELLIPSE_OPACITY_HIDDEN;
            }, "HasError subscription"))
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.VisualIcon)
            .Select(kind => kind != IconKind.None)
            .DistinctUntilChanged()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(hasIcon =>
            {
                ShowVisualIcon = hasIcon;
                ShowLeftSeparator = hasIcon;
            })
            .DisposeWith(_disposables);
    }

    private void UpdateVisualClasses()
    {
        if (_isDisposed)
        {
            return;
        }


        if (!string.IsNullOrEmpty(_currentStrengthClass))
        {
            _mainGrid.Classes.Remove(_currentStrengthClass);
            _currentStrengthClass = null;
        }

        if (IsSecureKeyStrengthMode)
        {
            if (StrengthClassMap.TryGetValue(SecureKeyStrength, out string? newClass))
            {
                _mainGrid.Classes.Add(newClass);
                _currentStrengthClass = newClass;
            }
        }
    }

    private void ToggleClass(string className, bool isEnabled)
    {
        if (isEnabled && !Classes.Contains(className))
        {
            Classes.Add(className);
        }
        else if (!isEnabled && Classes.Contains(className))
        {
            Classes.Remove(className);
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_isControlInitialized || _isDisposed)
        {
            return;
        }

        Initialize();
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

    private void UnsubscribeTextBoxEvents()
    {
        if (_mainTextBox == null)
        {
            return;
        }

        _mainTextBox.TextChanged -= OnTextChanged;

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

    #region Pointer & Gesture Handlers

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Handled = true;
        if (_mainTextBox != null)
        {
            _mainTextBox.CaretIndex = _mainTextBox.Text?.Length ?? 0;
            _intendedCaretPosition = _mainTextBox.Text?.Length ?? 0;
        }
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (_mainTextBox == null)
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
        e.Handled = true;
        if (_mainTextBox != null)
        {
            _mainTextBox.CaretIndex = _mainTextBox.Text?.Length ?? 0;
        }
    }

    private void OnHolding(object? sender, HoldingRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        e.Handled = true;
    }

    private void OnDragEnter(object? sender, DragEventArgs e) => HandleSecureKeyDragEvent(e);
    private void OnDragOver(object? sender, DragEventArgs e) => HandleSecureKeyDragEvent(e);
    private void OnDrop(object? sender, DragEventArgs e) => HandleSecureKeyDragEvent(e);

    private void HandleSecureKeyDragEvent(DragEventArgs e)
    {
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_mainTextBox == null)
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

    #endregion

    #region Input Validation & Key Handling

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        if (_mainTextBox != null)
        {
            if (_mainTextBox.SelectionStart != _mainTextBox.SelectionEnd ||
                _mainTextBox.CaretIndex != (_mainTextBox.Text?.Length ?? 0))
            {
                _mainTextBox.SelectionStart = _mainTextBox.Text?.Length ?? 0;
                _mainTextBox.SelectionEnd = _mainTextBox.Text?.Length ?? 0;
                _mainTextBox.CaretIndex = _mainTextBox.Text?.Length ?? 0;
            }
        }

        if (e.Text.Length > 1)
        {
            e.Handled = true;
            CharacterRejectedEventArgs multiCharArgs = new(CharacterWarningType.MULTIPLE_CHARACTERS)
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
            CharacterRejectedEventArgs args = new(warningType)
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

    private CharacterWarningType GetWarningType(char c)
    {
        if (char.IsLetter(c) && !(c is >= 'A' and <= 'Z' || c is >= 'a' and <= 'z'))
        {
            return CharacterWarningType.NON_LATIN_LETTER;
        }
        return CharacterWarningType.INVALID_CHARACTER;
    }

    private bool IsAllowedCharacter(char c)
    {
        if (char.IsWhiteSpace(c))
        {
            return false;
        }

        if (char.IsDigit(c))
        {
            return true;
        }

        if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
        {
            return true;
        }

        return !char.IsLetter(c) && !char.IsDigit(c);
    }

    private void StartWarningTimer()
    {
        if (_warningTimer == null)
        {
            _warningTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(WarningDisplayDurationMs) };
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
        if (HandleClipboardShortcuts(e))
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            e.Handled = true;

            CharacterRejectedEventArgs args = new(CharacterWarningType.INVALID_CHARACTER)
            {
                RoutedEvent = CharacterRejectedEvent
            };
            RaiseEvent(args);
            StartWarningTimer();
            return;
        }

        if (HandleNavigationKeys(e))
        {
            return;
        }

        if (HandleBackspaceKey(e))
        {
            return;
        }

        if (HandleDeleteKey(e))
        {
            return;
        }

        ResetCaretToEnd();
    }

    private bool HandleClipboardShortcuts(KeyEventArgs e)
    {
        bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        if (!isCtrl)
        {
            return false;
        }

        if (e.Key == Key.V)
        {
            e.Handled = true;
            HandlePasteAsync();
            return true;
        }

        if (IsClipboardKey(e.Key) || e.Key == Key.A)
        {
            e.Handled = true;
            return true;
        }

        return false;
    }
    private static bool IsClipboardKey(Key key) =>
        key is Key.C or Key.X or Key.Z or Key.Y or Key.Insert;

    private async void HandlePasteAsync()
    {
        if (_mainTextBox == null || _isDisposed)
        {
            return;
        }

        try
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null)
            {
                return;
            }

            string? clipboardText = await topLevel.Clipboard.GetTextAsync();

            if (_mainTextBox == null || _isDisposed)
            {
                return;
            }

            if (string.IsNullOrEmpty(clipboardText))
            {
                return;
            }

            foreach (char c in clipboardText)
            {
                if (!IsAllowedCharacter(c))
                {
                    CharacterRejectedEventArgs args = new(GetWarningType(c))
                    {
                        RoutedEvent = CharacterRejectedEvent
                    };
                    RaiseEvent(args);
                    StartWarningTimer();
                    return;
                }
            }

            int currentLength = _mainTextBox.Text?.Length ?? 0;
            if (currentLength + clipboardText.Length > MaxLength)
            {
                StartWarningTimer();
                return;
            }

            _mainTextBox.Text += clipboardText;

            _mainTextBox.CaretIndex = _mainTextBox.Text?.Length ?? 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Paste failed: {ex.Message}");
        }
    }

    private bool HandleNavigationKeys(KeyEventArgs e)
    {
        if (_mainTextBox == null)
        {
            return false;
        }

        return e.Key switch
        {
            Key.Back or Key.Delete or Key.End or Key.Home => false,
            Key.Left or Key.Right or Key.Up or Key.Down => HandleArrowKey(e),
            _ => false
        };
    }

    private static bool HandleArrowKey(KeyEventArgs e)
    {
        e.Handled = true;
        return true;
    }

    private bool HandleBackspaceKey(KeyEventArgs e)
    {
        if (e.Key != Key.Back)
        {
            return false;
        }

        e.Handled = true;
        RemoveLastCharacter();
        return true;
    }

    private bool HandleDeleteKey(KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
        {
            return false;
        }

        e.Handled = true;
        RemoveLastCharacter();
        return true;
    }

    private void RemoveLastCharacter()
    {
        if (_mainTextBox == null || string.IsNullOrEmpty(_mainTextBox.Text))
        {
            return;
        }

        string currentText = _mainTextBox.Text;
        string newText = currentText.Length > 0 ? currentText.Substring(0, currentText.Length - 1) : string.Empty;

        _isUpdatingFromCode = true;
        _mainTextBox.Text = newText;
        _mainTextBox.CaretIndex = newText.Length;
        _isUpdatingFromCode = false;

        _intendedCaretPosition = newText.Length;
    }

    private void ResetCaretToEnd()
    {
        if (_mainTextBox != null)
        {
            _mainTextBox.CaretIndex = _mainTextBox.Text?.Length ?? 0;
        }
    }

    #endregion

    #region Text Processing Logic

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingFromCode || _mainTextBox == null || _isDisposed)
        {
            return;
        }

        DebouncedProcessSecureKeyChange();
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
        _secureKeyDebounceTimer?.Stop();
        if (!_isDisposed)
        {
            ProcessSecureKeyChange();
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

            ProcessTextDifference(currentText, lastText);

            _lastProcessedText = currentText;
            _lastProcessedTextElementCount = GetTextElementCount(currentText);
            UpdateRemainingCharacters();
        }
        finally
        {
            ResetCaretPosition();
            _isProcessingSecureKeyChange = false;
        }
    }

    private void ProcessTextDifference(string currentText, string lastText)
    {
        try
        {
            ProcessTextDifferenceByElements(currentText, lastText);
        }
        catch
        {
            ProcessTextDifferenceByLength(currentText, lastText);
        }
    }

    private void ProcessTextDifferenceByElements(string currentText, string lastText)
    {
        int currentElementCount = GetTextElementCount(currentText);
        int lastElementCount = _lastProcessedTextElementCount > 0
            ? _lastProcessedTextElementCount
            : GetTextElementCount(lastText);

        if (currentElementCount > lastElementCount)
        {
            HandleCharactersAdded(currentText, lastText, currentElementCount, lastElementCount);
        }
        else if (currentElementCount < lastElementCount)
        {
            HandleCharactersRemoved(currentElementCount, lastElementCount);
        }
    }

    private void ProcessTextDifferenceByLength(string currentText, string lastText)
    {
        if (currentText.Length > lastText.Length)
        {
            int addedCount = currentText.Length - lastText.Length;
            int insertPos = Math.Max(HintedTextBoxConstants.INITIAL_CARET_INDEX, _mainTextBox!.CaretIndex - addedCount);
            string addedChars = SafeSubstring(currentText, insertPos, addedCount);

            _intendedCaretPosition = insertPos + addedCount;

            if (string.IsNullOrEmpty(addedChars))
            {
                return;
            }

            RaiseEvent(new SecureKeyCharactersAddedEventArgs(SecureKeyCharactersAddedEvent, insertPos, addedChars));
        }
        else if (currentText.Length < lastText.Length)
        {
            int removedCount = lastText.Length - currentText.Length;
            int removePos = _mainTextBox!.CaretIndex;

            _intendedCaretPosition = removePos;

            RaiseEvent(new SecureKeyCharactersRemovedEventArgs(SecureKeyCharactersRemovedEvent, removePos, removedCount));
        }
    }

    private void HandleCharactersAdded(string currentText, string lastText, int currentElementCount, int lastElementCount)
    {
        int addedCount = currentElementCount - lastElementCount;
        string addedChars = GetAddedTextElements(currentText, lastText, _mainTextBox!.CaretIndex);

        int insertPos = Math.Max(HintedTextBoxConstants.INITIAL_CARET_INDEX, _mainTextBox.CaretIndex - addedCount);

        _intendedCaretPosition = insertPos + addedCount;

        if (string.IsNullOrEmpty(addedChars))
        {
            return;
        }

        RaiseEvent(new SecureKeyCharactersAddedEventArgs(SecureKeyCharactersAddedEvent, insertPos, addedChars));
    }

    private void HandleCharactersRemoved(int currentElementCount, int lastElementCount)
    {
        int removedCount = lastElementCount - currentElementCount;
        _intendedCaretPosition = currentElementCount;

        RaiseEvent(new SecureKeyCharactersRemovedEventArgs(SecureKeyCharactersRemovedEvent, currentElementCount,
            removedCount));
    }

    private void ResetCaretPosition()
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

    private void UpdateRemainingCharacters()
    {
        int currentTextLength = _mainTextBox != null
            ? GetTextElementCount(_mainTextBox.Text ?? string.Empty)
            : 0;
        RemainingCharacters = MaxLength - currentTextLength;
    }

    #endregion

    #region Helpers (SafeSubstring, GetTextElementCount, SafeExecute)

    private int GetTextElementCount(string text)
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

            if (TextElementCountCache.Count < MAX_TEXT_ELEMENT_CACHE_SIZE)
            {
                TextElementCountCache[text] = count;
            }
            else
            {
                TextElementCountCache.Clear();
                TextElementCountCache[text] = count;
            }

            return count;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HINTED-PASSWORDBOX] Failed to get text element count: {ex.Message}");
            return text.Length;
        }
    }

    private string SafeSubstring(string text, int startIndex, int length)
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
        catch
        {
            try
            {
                int safeStart = Math.Min(startIndex, text.Length);
                int safeLength = Math.Min(length, text.Length - safeStart);
                return safeLength > 0 ? text.Substring(safeStart, safeLength) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    private string GetAddedTextElements(string? currentText, string lastText, int caretIndex)
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
        catch
        {
            int addedCount = currentText.Length - lastText.Length;
            if (addedCount <= 0)
            {
                return string.Empty;
            }

            int insertPos = Math.Max(0, caretIndex - addedCount);
            return SafeSubstring(currentText, insertPos, addedCount);
        }
    }

    private void SafeExecute(Action action, string context)
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR in {context}: {ex.Message}");
        }
    }

    private void FindControls()
    {
        _mainGrid = this.FindControl<Grid>("MainGrid");
        _mainTextBox = this.FindControl<TextBox>(HintedTextBoxConstants.MAIN_TEXT_BOX_NAME);
        _focusBorder = this.FindControl<Border>(HintedTextBoxConstants.FOCUS_BORDER_NAME);
        _mainBorder = this.FindControl<Border>(HintedTextBoxConstants.MAIN_BORDER_NAME);
        _shadowBorder = this.FindControl<Border>(HintedTextBoxConstants.SHADOW_BORDER_NAME);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    #endregion
}
