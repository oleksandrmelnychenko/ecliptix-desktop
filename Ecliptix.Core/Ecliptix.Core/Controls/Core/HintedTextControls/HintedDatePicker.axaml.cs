using System;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Ecliptix.Core.Controls.Constants;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Core.HintedTextControls;

public partial class HintedDatePicker : UserControl
{
    #region Constants & Fields

    private readonly CompositeDisposable _disposables = new();

    private CalendarDatePicker? _mainDatePicker;
    private Border? _focusBorder;
    private Border? _mainBorder;
    private Border? _shadowBorder;

    private bool _isDisposed;
    private bool _isControlInitialized;
    private bool _isUpdatingInternally;

    #endregion

    #region Styled Properties

    public static readonly StyledProperty<DateTimeOffset?> SelectedDateProperty =
        AvaloniaProperty.Register<HintedDatePicker, DateTimeOffset?>(
            nameof(SelectedDate),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> DateFormatProperty =
        AvaloniaProperty.Register<HintedDatePicker, string>(
            nameof(DateFormat),
            defaultValue: "dd.MM.yyyy");

    public static readonly StyledProperty<string> WatermarkProperty =
        AvaloniaProperty.Register<HintedDatePicker, string>(nameof(Watermark), string.Empty);

    public static readonly StyledProperty<string> HintProperty =
        AvaloniaProperty.Register<HintedDatePicker, string>(nameof(Hint), string.Empty);

    public static readonly StyledProperty<string> ErrorTextProperty =
        AvaloniaProperty.Register<HintedDatePicker, string>(nameof(ErrorText), string.Empty);

    public static readonly StyledProperty<bool> HasErrorProperty =
        AvaloniaProperty.Register<HintedDatePicker, bool>(nameof(HasError));

    public static readonly StyledProperty<double> EllipseOpacityProperty =
        AvaloniaProperty.Register<HintedDatePicker, double>(nameof(EllipseOpacity));

    public static readonly StyledProperty<IBrush> TextForegroundProperty =
        AvaloniaProperty.Register<HintedDatePicker, IBrush>(
            nameof(TextForeground), new SolidColorBrush(Colors.Black));

    public static readonly StyledProperty<IBrush> HintForegroundProperty =
       AvaloniaProperty.Register<HintedDatePicker, IBrush>(
           nameof(HintForeground), new SolidColorBrush(Color.Parse(HintedTextBoxConstants.FOCUS_COLOR_HEX)));

    public static readonly StyledProperty<IBrush> FocusBorderBrushProperty =
       AvaloniaProperty.Register<HintedDatePicker, IBrush>(
           nameof(FocusBorderBrush), new SolidColorBrush(Color.Parse(HintedTextBoxConstants.FOCUS_COLOR_HEX)));

    public static readonly StyledProperty<IBrush> MainBorderBrushProperty =
        AvaloniaProperty.Register<HintedDatePicker, IBrush>(
            nameof(MainBorderBrush), new SolidColorBrush(Color.Parse(HintedTextBoxConstants.FOCUS_COLOR_HEX)));

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<HintedDatePicker, CornerRadius>(
            nameof(CornerRadius), new CornerRadius(4));

    public new static readonly StyledProperty<IBrush> BackgroundProperty =
        AvaloniaProperty.Register<HintedDatePicker, IBrush>(
            nameof(Background), new SolidColorBrush(Colors.White));

    public new static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<HintedDatePicker, double>(nameof(FontSize), HintedTextBoxConstants.DEFAULT_FONT_SIZE);

    // Внутрішня властивість для CalendarDatePicker (DateTime?)
    public static readonly StyledProperty<DateTime?> InternalDateProperty =
        AvaloniaProperty.Register<HintedDatePicker, DateTime?>(
            nameof(InternalDate),
            defaultBindingMode: BindingMode.TwoWay);

    #endregion

    public HintedDatePicker()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    #region Properties Accessors

    public DateTimeOffset? SelectedDate
    {
        get => GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    private DateTime? InternalDate
    {
        get => GetValue(InternalDateProperty);
        set => SetValue(InternalDateProperty, value);
    }

    public string DateFormat
    {
        get => GetValue(DateFormatProperty);
        set => SetValue(DateFormatProperty, value);
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

    public string ErrorText
    {
        get => GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }

    public bool HasError
    {
        get => GetValue(HasErrorProperty);
        set => SetValue(HasErrorProperty, value);
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

    public IBrush FocusBorderBrush
    {
        get => GetValue(FocusBorderBrushProperty);
        set => SetValue(FocusBorderBrushProperty, value);
    }

    public IBrush MainBorderBrush
    {
        get => GetValue(MainBorderBrushProperty);
        set => SetValue(MainBorderBrushProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
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

    #endregion

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
            AttachedToVisualTree -= OnAttachedToVisualTree;

            try
            {
                _disposables.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR disposing subscriptions: {ex.Message}");
            }

            _mainDatePicker = null;
            _focusBorder = null;
            _mainBorder = null;
            _shadowBorder = null;

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
        SetupReactiveBindings();
        SetupDateConversion();

        _isControlInitialized = true;
    }

    private void SetupReactiveBindings()
    {
        // Логіка накопичення тексту помилки
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

        // Логіка видимості/прозорості елементів помилки
        this.WhenAnyValue(x => x.HasError)
            .DistinctUntilChanged()
            .Subscribe(hasError => SafeExecute(() =>
            {
                EllipseOpacity = hasError
                    ? HintedTextBoxConstants.DEFAULT_ELLIPSE_OPACITY_VISIBLE
                    : HintedTextBoxConstants.DEFAULT_ELLIPSE_OPACITY_HIDDEN;
            }, "HasError subscription"))
            .DisposeWith(_disposables);
    }

    private void SetupDateConversion()
    {
        // Конвертація DateTimeOffset? -> DateTime? для CalendarDatePicker
        this.WhenAnyValue(x => x.SelectedDate)
            .Subscribe(dateOffset =>
            {
                if (_isUpdatingInternally)
                {
                    return;
                }

                _isUpdatingInternally = true;
                try
                {
                    InternalDate = dateOffset?.DateTime;
                }
                finally
                {
                    _isUpdatingInternally = false;
                }
            })
            .DisposeWith(_disposables);

        // Конвертація DateTime? -> DateTimeOffset? назад
        this.WhenAnyValue(x => x.InternalDate)
            .Subscribe(dateTime =>
            {
                if (_isUpdatingInternally)
                {
                    return;
                }

                _isUpdatingInternally = true;
                try
                {
                    SelectedDate = dateTime.HasValue
                        ? new DateTimeOffset(dateTime.Value, TimeSpan.Zero)
                        : null;
                }
                finally
                {
                    _isUpdatingInternally = false;
                }
            })
            .DisposeWith(_disposables);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_isControlInitialized || _isDisposed)
        {
            return;
        }

        Initialize();
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
        _mainDatePicker = this.FindControl<CalendarDatePicker>("MainDatePicker");
        _focusBorder = this.FindControl<Border>(HintedTextBoxConstants.FOCUS_BORDER_NAME);
        _mainBorder = this.FindControl<Border>(HintedTextBoxConstants.MAIN_BORDER_NAME);
        _shadowBorder = this.FindControl<Border>(HintedTextBoxConstants.SHADOW_BORDER_NAME);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
