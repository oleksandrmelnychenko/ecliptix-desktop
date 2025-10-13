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
    public static readonly StyledProperty<bool> IsPasswordModeProperty =
        AvaloniaProperty.Register<HintedTextBox, bool>(nameof(IsSecureKeyMode));

    public static readonly StyledProperty<char> PasswordMaskCharProperty =
        AvaloniaProperty.Register<HintedTextBox, char>(nameof(SecureKeyMaskChar),
            HintedTextBoxConstants.DefaultMaskChar);

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(Text), string.Empty,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string> WatermarkProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(Watermark), string.Empty);

    public static readonly StyledProperty<string> HintProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(Hint), string.Empty);

    public static readonly StyledProperty<IBrush> FocusBorderBrushProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(
            nameof(FocusBorderBrush), new SolidColorBrush(Color.Parse(HintedTextBoxConstants.FocusColorHex)));

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
        AvaloniaProperty.Register<HintedTextBox, double>(nameof(FontSize), HintedTextBoxConstants.DefaultFontSize);

    public static readonly StyledProperty<double> WatermarkFontSizeProperty =
        AvaloniaProperty.Register<HintedTextBox, double>(nameof(WatermarkFontSize),
            HintedTextBoxConstants.DefaultWatermarkFontSize);

    public new static readonly StyledProperty<FontWeight> FontWeightProperty =
        AvaloniaProperty.Register<HintedTextBox, FontWeight>(nameof(FontWeight), FontWeight.Normal);

    public static readonly StyledProperty<bool> IsPasswordStrengthModeProperty =
        AvaloniaProperty.Register<HintedTextBox, bool>(nameof(IsPasswordStrengthMode));

    public static readonly StyledProperty<PasswordStrength> PasswordStrengthProperty =
        AvaloniaProperty.Register<HintedTextBox, PasswordStrength>(nameof(PasswordStrength), PasswordStrength.Invalid);

    public static readonly StyledProperty<string> PasswordStrengthTextProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(PasswordStrengthText), string.Empty);

    public static readonly StyledProperty<IBrush> PasswordStrengthTextBrushProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(nameof(PasswordStrengthTextBrush),
            new SolidColorBrush(Colors.Gray));

    public static readonly StyledProperty<IBrush> PasswordStrengthIconBrushProperty =
        AvaloniaProperty.Register<HintedTextBox, IBrush>(nameof(PasswordStrengthIconBrush),
            new SolidColorBrush(Colors.Gray));

    public static readonly StyledProperty<string> WarningTextProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(WarningText), string.Empty);

    public static readonly StyledProperty<bool> HasWarningProperty =
        AvaloniaProperty.Register<HintedTextBox, bool>(nameof(HasWarning));


    public static readonly StyledProperty<int> WarningDisplayDurationMsProperty =
        AvaloniaProperty.Register<HintedTextBox, int>(nameof(WarningDisplayDurationMs),
            HintedTextBoxConstants.DefaultWarningDisplayDurationMs);


    public static readonly RoutedEvent<CharacterRejectedEventArgs> CharacterRejectedEvent =
        RoutedEvent.Register<HintedTextBox, CharacterRejectedEventArgs>(nameof(CharacterRejected), RoutingStrategies.Bubble);

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

    public int WarningDisplayDurationMs
    {
        get => GetValue(WarningDisplayDurationMsProperty);
        set => SetValue(WarningDisplayDurationMsProperty, value);
    }

    public bool IsSecureKeyMode
    {
        get => GetValue(IsPasswordModeProperty);
        set
        {
            bool oldValue = GetValue(IsPasswordModeProperty);
            SetValue(IsPasswordModeProperty, value);
            if (oldValue != value)
            {
                OnIsSecureKeyModeChanged(value);
            }
        }
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

    private readonly CompositeDisposable _disposables = new();
    private TextBox? _mainTextBox;
    private Border? _focusBorder;
    private Border? _mainBorder;
    private Border? _shadowBorder;
    private bool _isUpdatingFromCode;
    private bool _isDisposed;
    private bool _isControlInitialized;
    private DispatcherTimer? _warningTimer;


    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new();
    private static readonly Dictionary<string, Color> ColorCache = new();
    private static readonly Dictionary<string, BoxShadows> ResourceCache = new();
    private static readonly Dictionary<string, int> TextElementCountCache = new(StringComparer.Ordinal);
    private const int MaxTextElementCacheSize = 1000;

    private DispatcherTimer? _inputDebounceTimer;
    private DispatcherTimer? _secureKeyDebounceTimer;
    private const int InputDebounceDelayMs = HintedTextBoxConstants.InputDebounceDelayMs;
    private const int SecureKeyDebounceDelayMs = 50;

    private bool _lastIsFocused;
    private bool _lastHasError;
    private PasswordStrength _lastPasswordStrength = PasswordStrength.Invalid;
    private bool _lastIsPasswordStrengthMode;

    private string _lastProcessedText = string.Empty;
    private IDisposable? _currentTypingAnimation;
    private int _lastProcessedTextElementCount;
    private volatile bool _isProcessingSecureKeyChange;
    private int _intendedCaretPosition;
    private string _originalErrorText = string.Empty;

    public HintedTextBox()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    public void SyncSecureKeyState(int newPasswordLength)
    {
        if (_mainTextBox == null) return;

        string maskText = newPasswordLength > 0
            ? new string(SecureKeyMaskChar, newPasswordLength)
            : string.Empty;

        _mainTextBox.PasswordChar = HintedTextBoxConstants.NoPasswordChar;

        int caretPosition = Math.Clamp(_intendedCaretPosition, 0, newPasswordLength);
        UpdateTextBox(maskText, caretPosition);
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
        if (_mainTextBox == null) return;

        _mainTextBox.AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        _mainTextBox.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        _mainTextBox.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        _mainTextBox.IsReadOnly = false;
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (!IsSecureKeyMode) return;

        if (string.IsNullOrEmpty(e.Text)) return;

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
        }

        if (_mainTextBox != null)
        {
            int currentLength = _mainTextBox.Text?.Length ?? 0;
            if (_mainTextBox.CaretIndex < currentLength)
            {
                e.Handled = true;
                _mainTextBox.CaretIndex = currentLength;
            }
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

    private static CharacterWarningType GetWarningType(char c)
    {
        if (char.IsLetter(c) && !((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')))
            return CharacterWarningType.NonLatinLetter;

        return CharacterWarningType.InvalidCharacter;
    }

    private static bool IsAllowedCharacter(char c)
    {
        if (char.IsDigit(c))
            return true;

        if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
            return true;

        if (!char.IsLetter(c) && !char.IsDigit(c))
            return true;

        return false;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsSecureKeyMode) return;

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
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsSecureKeyMode || _mainTextBox is null || _isUpdatingFromCode)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_mainTextBox is null || _isUpdatingFromCode)
                return;

            _isUpdatingFromCode = true;

            int textLength = _mainTextBox.Text?.Length ?? 0;
            int selLength = Math.Abs(_mainTextBox.SelectionEnd - _mainTextBox.SelectionStart);

            bool isFullSelection = selLength == textLength;
            if (!isFullSelection)
            {
                _mainTextBox.ClearSelection();
                _mainTextBox.CaretIndex = textLength - selLength;
                _mainTextBox.CaretIndex = textLength;
            }

            _isUpdatingFromCode = false;
        }, DispatcherPriority.Render);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingFromCode || _mainTextBox == null || _isDisposed) return;

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
                Interval = TimeSpan.FromMilliseconds(InputDebounceDelayMs)
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
                Interval = TimeSpan.FromMilliseconds(SecureKeyDebounceDelayMs)
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
            System.Diagnostics.Debug.WriteLine($"Error in OnSecureKeyDebounceTimerTick: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"Error in OnDebounceTimerTick: {ex.Message}");
        }
    }

    private void ProcessSecureKeyChange()
    {
        if (_isUpdatingFromCode || _mainTextBox == null || _isDisposed || _isProcessingSecureKeyChange) return;

        _isProcessingSecureKeyChange = true;
        try
        {
            string currentText = _mainTextBox.Text ?? string.Empty;
            string lastText = _lastProcessedText;

            if (currentText == lastText) return;

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

                    int insertPos = Math.Max(HintedTextBoxConstants.InitialCaretIndex, _mainTextBox.CaretIndex - addedCount);

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
                    int removePos = Math.Max(HintedTextBoxConstants.InitialCaretIndex, _mainTextBox.CaretIndex);

                    _intendedCaretPosition = removePos;

                    RaiseEvent(
                        new SecureKeyCharactersRemovedEventArgs(SecureKeyCharactersRemovedEvent, removePos,
                            removedCount));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ProcessSecureKeyChange: {ex.Message}");

                if (currentText.Length > lastText.Length)
                {
                    int addedCount = currentText.Length - lastText.Length;
                    int insertPos = Math.Max(HintedTextBoxConstants.InitialCaretIndex, _mainTextBox.CaretIndex - addedCount);
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
            _isProcessingSecureKeyChange = false;
        }
    }

    private void ProcessTextChange()
    {
        if (_isUpdatingFromCode || _mainTextBox == null || _isDisposed) return;
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

        if (!IsSecureKeyMode && GetTextElementCount(input) > GetTextElementCount(Text ?? string.Empty) -
            HintedTextBoxConstants.TypingAnimationThreshold)
        {
            TriggerTypingAnimation();
        }
    }

    private void UpdateTextBox(string? text, int caretIndex)
    {
        if (_mainTextBox == null) return;

        text ??= string.Empty;
        _isUpdatingFromCode = true;
        _mainTextBox.Text = text;
        _mainTextBox.CaretIndex = Math.Clamp(caretIndex, HintedTextBoxConstants.InitialCaretIndex, text.Length);
        _isUpdatingFromCode = false;
    }

    private void UpdateBorderState()
    {
        if (_mainTextBox == null || _focusBorder == null || _mainBorder == null || _shadowBorder == null)
            return;

        bool currentHasError = HasError;
        PasswordStrength currentPasswordStrength = PasswordStrength;
        bool currentIsPasswordStrengthMode = IsPasswordStrengthMode;

        Dispatcher.UIThread.Post(() =>
        {
            if (_isDisposed) return;
            if (_mainTextBox == null || _focusBorder == null || _mainBorder == null || _shadowBorder == null) return;

            bool isFocused = _mainTextBox.IsFocused;
            if (isFocused == _lastIsFocused
                && currentHasError == _lastHasError
                && currentPasswordStrength == _lastPasswordStrength
                && currentIsPasswordStrengthMode == _lastIsPasswordStrengthMode)
            {
                return;
            }

            _lastIsFocused = isFocused;
            _lastHasError = currentHasError;
            _lastPasswordStrength = currentPasswordStrength;
            _lastIsPasswordStrengthMode = currentIsPasswordStrengthMode;

            UpdateBorderStateInternal(isFocused, currentHasError, currentPasswordStrength, currentIsPasswordStrengthMode);
        }, DispatcherPriority.Render);
    }

    private void UpdateBorderStateInternal(bool isFocused, bool hasError, PasswordStrength passwordStrength, bool isPasswordStrengthMode)
    {
        if (_focusBorder == null || _mainBorder == null || _shadowBorder == null)
            return;

        if (isPasswordStrengthMode)
        {
            (Color borderColor, string shadowKey, Color iconColor) = GetPasswordStrengthColors(passwordStrength);

            _focusBorder.BorderBrush = GetCachedBrush(borderColor);
            _focusBorder.Opacity = HintedTextBoxConstants.FullOpacity;
            _mainBorder.BorderBrush = GetCachedBrush(Colors.Transparent);
            _shadowBorder.BoxShadow = GetCachedResource(shadowKey);

            PasswordStrengthIconBrush = GetCachedBrush(iconColor);
            PasswordStrengthTextBrush = GetCachedBrush(iconColor);
        }
        else if (hasError)
        {
            _focusBorder.BorderBrush = GetCachedBrush(GetCachedColor(HintedTextBoxConstants.ErrorColorHex));
            _focusBorder.Opacity = HintedTextBoxConstants.FullOpacity;
            _mainBorder.BorderBrush = GetCachedBrush(Colors.Transparent);
            _shadowBorder.BoxShadow = GetCachedResource(HintedTextBoxConstants.ErrorShadowKey);
        }
        else if (isFocused)
        {
            _focusBorder.BorderBrush = FocusBorderBrush;
            _focusBorder.Opacity = HintedTextBoxConstants.FullOpacity;
            _mainBorder.BorderBrush = GetCachedBrush(Colors.Transparent);
            _shadowBorder.BoxShadow = GetCachedResource(HintedTextBoxConstants.FocusShadowKey);
        }
        else
        {
            _focusBorder.Opacity = HintedTextBoxConstants.ZeroOpacity;
            _mainBorder.BorderBrush = MainBorderBrush;
            _shadowBorder.BoxShadow = GetCachedResource(HintedTextBoxConstants.DefaultShadowKey);
        }
    }

    private static (Color BorderColor, string ShadowKey, Color IconColor) GetPasswordStrengthColors(
        PasswordStrength strength)
    {
        return strength switch
        {
            PasswordStrength.Invalid => (GetCachedColor(HintedTextBoxConstants.InvalidStrengthColorHex),
                HintedTextBoxConstants.InvalidStrengthShadowKey,
                GetCachedColor(HintedTextBoxConstants.InvalidStrengthColorHex)),
            PasswordStrength.VeryWeak => (GetCachedColor(HintedTextBoxConstants.VeryWeakStrengthColorHex),
                HintedTextBoxConstants.VeryWeakStrengthShadowKey,
                GetCachedColor(HintedTextBoxConstants.VeryWeakStrengthColorHex)),
            PasswordStrength.Weak => (GetCachedColor(HintedTextBoxConstants.WeakStrengthColorHex),
                HintedTextBoxConstants.WeakStrengthShadowKey,
                GetCachedColor(HintedTextBoxConstants.WeakStrengthColorHex)),
            PasswordStrength.Good => (GetCachedColor(HintedTextBoxConstants.GoodStrengthColorHex),
                HintedTextBoxConstants.GoodStrengthShadowKey,
                GetCachedColor(HintedTextBoxConstants.GoodStrengthColorHex)),
            PasswordStrength.Strong => (GetCachedColor(HintedTextBoxConstants.StrongStrengthColorHex),
                HintedTextBoxConstants.StrongStrengthShadowKey,
                GetCachedColor(HintedTextBoxConstants.StrongStrengthColorHex)),
            PasswordStrength.VeryStrong => (GetCachedColor(HintedTextBoxConstants.VeryStrongStrengthColorHex),
                HintedTextBoxConstants.VeryStrongStrengthShadowKey,
                GetCachedColor(HintedTextBoxConstants.VeryStrongStrengthColorHex)),
            _ => (GetCachedColor(HintedTextBoxConstants.InvalidStrengthColorHex),
                HintedTextBoxConstants.InvalidStrengthShadowKey,
                GetCachedColor(HintedTextBoxConstants.InvalidStrengthColorHex))
        };
    }

    private void UnsubscribeTextBoxEvents()
    {
        if (_mainTextBox == null) return;
        _mainTextBox.TextChanged -= OnTextChanged;
        _mainTextBox.GotFocus -= OnGotFocus;
        _mainTextBox.LostFocus -= OnLostFocus;
        _mainTextBox.RemoveHandler(TextInputEvent, OnTextInput);
        _mainTextBox.RemoveHandler(KeyDownEvent, OnPreviewKeyDown);
        _mainTextBox.RemoveHandler(PointerReleasedEvent, OnPointerReleased);
    }

    private void FindControls()
    {
        _mainTextBox = this.FindControl<TextBox>(HintedTextBoxConstants.MainTextBoxName);
        _focusBorder = this.FindControl<Border>(HintedTextBoxConstants.FocusBorderName);
        _mainBorder = this.FindControl<Border>(HintedTextBoxConstants.MainBorderName);
        _shadowBorder = this.FindControl<Border>(HintedTextBoxConstants.ShadowBorderName);
    }

    private void OnGotFocus(object? sender, GotFocusEventArgs e)
    {
        try
        {
            if (!_isDisposed)
                UpdateBorderState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnGotFocus: {ex.Message}");
        }
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!_isDisposed)
                UpdateBorderState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnLostFocus: {ex.Message}");
        }
    }

    private void SetupReactiveBindings()
    {
        this.WhenAnyValue(
                x => x.HasError,
                x => x.PasswordStrength,
                x => x.IsPasswordStrengthMode,
                (hasError, passwordStrength, isPasswordStrengthMode) => (hasError, passwordStrength, isPasswordStrengthMode))
            .DistinctUntilChanged()
            .Subscribe(_ =>
            {
                try
                {
                    if (!_isDisposed)
                        UpdateBorderState();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in UpdateBorderState subscription: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine($"Error in ErrorText subscription: {ex.Message}");
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
                        UpdateTextBox(text, (text).Length);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in Text subscription: {ex.Message}");
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
                        EllipseOpacity = hasError
                            ? HintedTextBoxConstants.DefaultEllipseOpacityVisible
                            : HintedTextBoxConstants.DefaultEllipseOpacityHidden;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in HasError subscription: {ex.Message}");
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
        if (_mainTextBox == null) return;

        _mainTextBox.PasswordChar = HintedTextBoxConstants.NoPasswordChar;

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
        if (_mainTextBox == null) return;

        _mainTextBox.RemoveHandler(TextInputEvent, OnTextInput);
        _mainTextBox.RemoveHandler(KeyDownEvent, OnPreviewKeyDown);
        _mainTextBox.RemoveHandler(PointerReleasedEvent, OnPointerReleased);
    }

    private void TriggerTypingAnimation()
    {
        if (_mainTextBox == null || _isDisposed || _focusBorder == null) return;

        try
        {
            _currentTypingAnimation?.Dispose();

            if (!(_focusBorder.Opacity > HintedTextBoxConstants.ZeroOpacity)) return;
            Animation pulseAnimation = new()
            {
                Duration = TimeSpan.FromMilliseconds(HintedTextBoxConstants.TypingAnimationDurationMs),
                FillMode = FillMode.None,
                Easing = new CubicEaseOut()
            };

            KeyFrame startFrame = new()
            {
                Cue = Cue.Parse(HintedTextBoxConstants.AnimationStartPercent, CultureInfo.InvariantCulture),
                Setters = { new Setter { Property = OpacityProperty, Value = _focusBorder.Opacity } }
            };

            KeyFrame brightFrame = new()
            {
                Cue = Cue.Parse(HintedTextBoxConstants.AnimationPeakPercent, CultureInfo.InvariantCulture),
                Setters =
                {
                    new Setter
                    {
                        Property = OpacityProperty,
                        Value = Math.Min(HintedTextBoxConstants.FullOpacity,
                            _focusBorder.Opacity + HintedTextBoxConstants.AnimationOpacityBoost)
                    }
                }
            };

            KeyFrame endFrame = new()
            {
                Cue = Cue.Parse(HintedTextBoxConstants.AnimationEndPercent, CultureInfo.InvariantCulture),
                Setters = { new Setter { Property = OpacityProperty, Value = _focusBorder.Opacity } }
            };

            pulseAnimation.Children.Add(startFrame);
            pulseAnimation.Children.Add(brightFrame);
            pulseAnimation.Children.Add(endFrame);

            _currentTypingAnimation = pulseAnimation.RunAsync(_focusBorder);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in TriggerTypingAnimation: {ex.Message}");
        }
    }

    private static int GetTextElementCount(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

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
                case < MaxTextElementCacheSize:
                    TextElementCountCache[text] = count;
                    break;
                case > MaxTextElementCacheSize:
                    TextElementCountCache.Clear();
                    TextElementCountCache[text] = count;
                    break;
            }

            return count;
        }
        catch (Exception)
        {
            int fallbackCount = text.Length;
            if (TextElementCountCache.Count < MaxTextElementCacheSize)
            {
                TextElementCountCache[text] = fallbackCount;
            }

            return fallbackCount;
        }
    }

    private static string SafeSubstring(string text, int startIndex, int length)
    {
        if (string.IsNullOrEmpty(text) || startIndex < 0) return string.Empty;

        try
        {
            StringInfo stringInfo = new(text);
            int textElementCount = stringInfo.LengthInTextElements;

            if (startIndex >= textElementCount) return string.Empty;

            int actualLength = Math.Min(length, textElementCount - startIndex);
            return actualLength <= 0 ? string.Empty : stringInfo.SubstringByTextElements(startIndex, actualLength);
        }
        catch (Exception)
        {
            try
            {
                int safeStart = Math.Min(startIndex, text.Length);
                int safeLength = Math.Min(length, text.Length - safeStart);
                return safeLength > 0 ? text.Substring(safeStart, safeLength) : string.Empty;
            }
            catch (Exception)
            {
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

            if (currentCount <= lastCount) return string.Empty;

            int addedCount = currentCount - lastCount;
            int insertPos = Math.Max(0, Math.Min(caretIndex - addedCount, currentCount - addedCount));

            return SafeSubstring(currentText, insertPos, addedCount);
        }
        catch (Exception)
        {
            int addedCount = currentText.Length - lastText.Length;
            if (addedCount <= 0) return string.Empty;

            int insertPos = Math.Max(0, caretIndex - addedCount);
            return SafeSubstring(currentText, insertPos, addedCount);
        }
    }


    private static Color GetCachedColor(string colorHex)
    {
        if (ColorCache.TryGetValue(colorHex, out Color color)) return color;
        color = Color.Parse(colorHex);
        ColorCache[colorHex] = color;

        return color;
    }

    private static SolidColorBrush GetCachedBrush(Color color)
    {
        string key = color.ToString();
        if (BrushCache.TryGetValue(key, out SolidColorBrush? brush)) return brush;
        brush = new SolidColorBrush(color);
        BrushCache[key] = brush;

        return brush;
    }

    private BoxShadows GetCachedResource(string resourceKey)
    {
        if (ResourceCache.TryGetValue(resourceKey, out BoxShadows shadow)) return shadow;
        shadow = this.FindResource(resourceKey) is BoxShadows foundShadow ? foundShadow : default;
        ResourceCache[resourceKey] = shadow;

        return shadow;
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
                System.Diagnostics.Debug.WriteLine($"Error disposing subscriptions: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"Error in Dispose: {ex.Message}");
        }
    }
}
