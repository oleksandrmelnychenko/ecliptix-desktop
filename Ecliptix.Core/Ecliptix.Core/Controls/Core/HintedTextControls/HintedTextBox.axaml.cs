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

public sealed partial class HintedTextBox : UserControl, IDisposable
{
    #region Constants & Fields

    private const string CLASS_ERROR = "error";

    private readonly CompositeDisposable _disposables = new();

    private TextBox? _mainTextBox;
    private Border? _focusBorder;
    private Border? _mainBorder;
    private Border? _shadowBorder;

    private bool _isDisposed;
    private bool _isControlInitialized;

    #endregion

    #region Styled Properties

    public static readonly StyledProperty<string> WatermarkProperty =
        AvaloniaProperty.Register<HintedPasswordBox, string>(nameof(Watermark), string.Empty);

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<HintedTextBox, string>(nameof(Text), string.Empty,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> HintProperty =
        AvaloniaProperty.Register<HintedPasswordBox, string>(nameof(Hint), string.Empty);

    public static readonly StyledProperty<DrawingImage?> IconRegularSourceProperty =
        AvaloniaProperty.Register<HintedPasswordBox, DrawingImage?>(nameof(IconRegularSource));

    public static readonly StyledProperty<DrawingImage?> IconErrorSourceProperty =
        AvaloniaProperty.Register<HintedPasswordBox, DrawingImage?>(nameof(IconErrorSource));

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
            nameof(MainBorderBrush), new SolidColorBrush(Color.Parse("#4DFF6D00")));

    public static readonly StyledProperty<int> MaxLengthProperty =
        AvaloniaProperty.Register<HintedPasswordBox, int>(nameof(MaxLength), int.MaxValue);

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

    #endregion

    public HintedTextBox()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
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

            _mainTextBox = null;
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
        if (_mainTextBox == null)
        {
            return;
        }

        SetupReactiveBindings();
        UpdateVisualClasses();

        _isControlInitialized = true;
    }

    private void SetupReactiveBindings()
    {
        this.WhenAnyValue(
                x => x.HasError)
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
    }

    private void UpdateVisualClasses()
    {
        if (_isDisposed)
        {
            return;
        }

        ToggleClass(CLASS_ERROR, HasError);
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
        _mainTextBox = this.FindControl<TextBox>(HintedTextBoxConstants.MAIN_TEXT_BOX_NAME);
        _focusBorder = this.FindControl<Border>(HintedTextBoxConstants.FOCUS_BORDER_NAME);
        _mainBorder = this.FindControl<Border>(HintedTextBoxConstants.MAIN_BORDER_NAME);
        _shadowBorder = this.FindControl<Border>(HintedTextBoxConstants.SHADOW_BORDER_NAME);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

}
