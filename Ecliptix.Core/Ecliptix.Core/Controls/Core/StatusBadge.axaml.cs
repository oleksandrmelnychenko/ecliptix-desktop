using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Ecliptix.Core.Controls.Core;

public partial class StatusBadge : UserControl
{
    // --- Styled Properties ---
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<StatusBadge, string>(nameof(Text));
    public static readonly StyledProperty<Geometry> IconProperty = AvaloniaProperty.Register<StatusBadge, Geometry>(nameof(Icon));
    public static readonly StyledProperty<IBrush> BadgeBrushProperty = AvaloniaProperty.Register<StatusBadge, IBrush>(nameof(BadgeBrush), Brushes.Gray);
    public static readonly StyledProperty<IBrush> BadgeBackgroundProperty = AvaloniaProperty.Register<StatusBadge, IBrush>(nameof(BadgeBackground), Brushes.Transparent);
    public static readonly StyledProperty<string?> HoverTextProperty = AvaloniaProperty.Register<StatusBadge, string?>(nameof(HoverText));

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? HoverText { get => GetValue(HoverTextProperty); set => SetValue(HoverTextProperty, value); }
    public Geometry Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public IBrush BadgeBrush { get => GetValue(BadgeBrushProperty); set => SetValue(BadgeBrushProperty, value); }
    public IBrush BadgeBackground { get => GetValue(BadgeBackgroundProperty); set => SetValue(BadgeBackgroundProperty, value); }

    // --- UI Elements ---
    private Border? _containerBorder;
    private TextBlock? _measuringBlock;

    // --- Logic ---
    private CancellationTokenSource? _animCts;
    private string? _originalText;

    // --- CACHED VALUES (Зберігаємо параметри тут) ---
    private Thickness _cachedPadding;
    private Thickness _cachedBorderThickness;
    private double _cachedIconSize;
    private double _cachedIconSpacing;
    private TimeSpan _cachedAnimDuration;
    private TimeSpan _cachedColorDuration;

    // --- Resource Keys ---
    private const string DurationKey = "BadgeAnimationDuration";
    private const string ColorDurationKey = "BadgeColorDuration";
    private const string PaddingKey = "BadgePadding";
    private const string BorderKey = "BadgeBorderThickness";
    private const string IconSizeKey = "BadgeIconSize";
    private const string IconSpacingKey = "BadgeIconSpacing";

    public StatusBadge()
    {
        InitializeComponent();
        _containerBorder = this.FindControl<Border>("ContainerBorder");
        _measuringBlock = this.FindControl<TextBlock>("MeasuringBlock");
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // Цей метод викликається один раз, коли контрол додається на екран
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Тут ми "запікаємо" значення.
        // DefaultValue (другий параметр) МАЄ співпадати з XAML, про всяк випадок.
        _cachedPadding = GetResourceValue(PaddingKey, new Thickness(8, 0));
        _cachedBorderThickness = GetResourceValue(BorderKey, new Thickness(1));
        _cachedIconSize = GetResourceValue(IconSizeKey, 10.0);
        _cachedIconSpacing = GetResourceValue(IconSpacingKey, 2.0);

        _cachedAnimDuration = GetResourceValue(DurationKey, TimeSpan.FromSeconds(0.3));
        _cachedColorDuration = GetResourceValue(ColorDurationKey, TimeSpan.FromSeconds(0.2));
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (!string.IsNullOrEmpty(HoverText) && HoverText != Text)
        {
            _originalText = Text;
            AnimateToNewText(HoverText);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_originalText != null)
        {
            AnimateToNewText(_originalText);
            _originalText = null;
        }
    }

    private async void AnimateToNewText(string newText)
    {
        if (_containerBorder == null || _measuringBlock == null)
        {
            return;
        }

        _animCts?.Cancel();
        _animCts = new CancellationTokenSource();
        CancellationToken token = _animCts.Token;

        try
        {
            // 1. Фіксація поточної ширини
            double currentVisualWidth = _containerBorder.Bounds.Width;

            // Якщо контрол ще не видний - просто сетим текст
            if (currentVisualWidth <= 0 || double.IsNaN(currentVisualWidth))
            {
                Text = newText;
                return;
            }

            _containerBorder.Transitions = null;
            _containerBorder.Width = currentVisualWidth;

            // Коротка пауза для рендера
            await Task.Delay(15, token);

            // 2. Розрахунок нової ширини (використовує кеш)
            double targetWidth = CalculateRequiredWidth(newText);

            // 3. Увімкнення анімації (використовує кеш часу)
            UpdateTransitions();

            // 4. Старт
            Text = newText;
            _containerBorder.Width = targetWidth;

            // 5. Очікування
            int delayMs = (int)_cachedAnimDuration.TotalMilliseconds + 50;
            await Task.Delay(delayMs, token);

            // 6. Завершення
            if (!token.IsCancellationRequested)
            {
                // Вимикаємо анімацію перед скиданням в Auto, щоб не було зворотнього руху
                _containerBorder.Transitions = null;
                _containerBorder.Width = double.NaN;

                // Повертаємо транзішни назад
                await Task.Delay(20, token);
                UpdateTransitions();
            }
        }
        catch (TaskCanceledException) { }
    }

    private void UpdateTransitions()
    {
        if (_containerBorder == null)
        {
            return;
        }

        // Якщо транзішни вже є - не перестворюємо
        if (_containerBorder.Transitions != null && _containerBorder.Transitions.Count > 0)
        {
            return;
        }

        _containerBorder.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Layoutable.WidthProperty,
                Duration = _cachedAnimDuration,
                Easing = new CubicEaseOut()
            },
            new BrushTransition
            {
                Property = TemplatedControl.BackgroundProperty,
                Duration = _cachedColorDuration
            },
            new BrushTransition
            {
                Property = TemplatedControl.BorderBrushProperty,
                Duration = _cachedColorDuration
            }
        };
    }

    private double CalculateRequiredWidth(string text)
    {
        if (_measuringBlock == null)
        {
            return 0;
        }

        _measuringBlock.Text = text;
        _measuringBlock.Measure(Size.Infinity);

        // Тут ми беремо реальну ширину тексту
        double textWidth = _measuringBlock.DesiredSize.Width;

        // І додаємо КЕШОВАНІ відступи.
        // Оскільки вони 1-в-1 з XAML, математика зійдеться ідеально.
        double totalWidth = textWidth + _cachedPadding.Left + _cachedPadding.Right
                                      + _cachedBorderThickness.Left + _cachedBorderThickness.Right;

        if (Icon != null)
        {
            totalWidth += _cachedIconSize + _cachedIconSpacing;
        }

        return totalWidth;
    }

    private T GetResourceValue<T>(string key, T defaultValue)
    {
        if (this.TryGetResource(key, null, out object? res) && res is T typedRes)
        {
            return typedRes;
        }
        return defaultValue;
    }
}

