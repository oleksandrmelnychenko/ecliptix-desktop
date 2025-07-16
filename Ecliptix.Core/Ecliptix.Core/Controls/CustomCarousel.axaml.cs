using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Ecliptix.Core.ViewModels.Memberships;
using ReactiveUI;

namespace Ecliptix.Core.Controls;

public partial class CustomCarousel : UserControl
{
    private ItemsControl _indicatorsControl;
    private ScrollViewer _scrollViewer;
    private ItemsControl _carouselItemsControl;
    private List<double> _itemCenters;

    private IDisposable _itemsSourceSubscription;
    private IDisposable _selectedIndexSubscription;
    private IDisposable _cardWidthSubscription;
    private IDisposable _cardHeightSubscription;
    private IDisposable _cardSpacingSubscription;

    #region Styled Properties

    public static readonly StyledProperty<IEnumerable> ItemsSourceProperty =
        AvaloniaProperty.Register<CustomCarousel, IEnumerable>(nameof(ItemsSource));

    public static readonly StyledProperty<int> SelectedIndexProperty = AvaloniaProperty.Register<
        CustomCarousel,
        int
    >(nameof(SelectedIndex), 0);

    public static readonly StyledProperty<double> CardWidthProperty = AvaloniaProperty.Register<
        CustomCarousel,
        double
    >(nameof(CardWidth), 240.0);

    public static readonly StyledProperty<double> CardHeightProperty = AvaloniaProperty.Register<
        CustomCarousel,
        double
    >(nameof(CardHeight), 280.0);

    public static readonly StyledProperty<double> CardSpacingProperty = AvaloniaProperty.Register<
        CustomCarousel,
        double
    >(nameof(CardSpacing), 24.0);

    public static readonly StyledProperty<double> ContainerWidthProperty =
        AvaloniaProperty.Register<CustomCarousel, double>(nameof(ContainerWidth), 0.0);

    #endregion

    #region Public Properties

    public IEnumerable ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public double CardWidth
    {
        get => GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public double CardHeight
    {
        get => GetValue(CardHeightProperty);
        set => SetValue(CardHeightProperty, value);
    }

    public double CardSpacing
    {
        get => GetValue(CardSpacingProperty);
        set => SetValue(CardSpacingProperty, value);
    }

    public double ContainerWidth
    {
        get => GetValue(ContainerWidthProperty);
        set => SetValue(ContainerWidthProperty, value);
    }

    #endregion

    public CustomCarousel()
    {
        AvaloniaXamlLoader.Load(this);
        InitializeControls();
        SetupEventHandlers();
    }

    private void InitializeControls()
    {
        _indicatorsControl = this.FindControl<ItemsControl>("SlideIndicators");
        _scrollViewer = this.FindControl<ScrollViewer>("CarouselScrollViewer");
        _carouselItemsControl = this.FindControl<ItemsControl>("CarouselItemsControl");

        _itemCenters = new List<double>();
    }

    private void SetupEventHandlers()
    {
        if (_scrollViewer != null)
        {
            _scrollViewer.PointerWheelChanged += OnPointerWheelChanged;
        }

        this.SizeChanged += OnSizeChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Set up subscriptions when control is attached to visual tree
        _itemsSourceSubscription = this.GetObservable(ItemsSourceProperty)
            .Subscribe(OnItemsSourceChanged);
        _selectedIndexSubscription = this.GetObservable(SelectedIndexProperty)
            .Subscribe(OnSelectedIndexChanged);
        _cardWidthSubscription = this.GetObservable(CardWidthProperty)
            .Subscribe(_ => OnCarouselPropertiesChanged());
        _cardHeightSubscription = this.GetObservable(CardHeightProperty)
            .Subscribe(_ => OnCarouselPropertiesChanged());
        _cardSpacingSubscription = this.GetObservable(CardSpacingProperty)
            .Subscribe(_ => OnCarouselPropertiesChanged());

        Dispatcher.UIThread.Post(
            () =>
            {
                OnItemsSourceChanged(ItemsSource);

                Dispatcher.UIThread.Post(
                    () =>
                    {
                        UpdateSlideIndicators(SelectedIndex);
                    },
                    DispatcherPriority.Render
                );

                OnCarouselPropertiesChanged();
            },
            DispatcherPriority.Loaded
        );
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // Clean up subscriptions when control is detached
        _itemsSourceSubscription?.Dispose();
        _selectedIndexSubscription?.Dispose();
        _cardWidthSubscription?.Dispose();
        _cardHeightSubscription?.Dispose();
        _cardSpacingSubscription?.Dispose();

        // Reset subscription fields
        _itemsSourceSubscription = null;
        _selectedIndexSubscription = null;
        _cardWidthSubscription = null;
        _cardHeightSubscription = null;
        _cardSpacingSubscription = null;
    }

    #region Core Calculation Functions

    /// <summary>
    /// Calculate symmetric margin to center the first item in the viewport
    /// </summary>
    /// <param name="itemWidth">Width of a single carousel item</param>
    /// <param name="viewportWidth">Width of the container viewport</param>
    /// <returns>Margin value for symmetric centering</returns>
    private double CalculateSymmetricMargin(double itemWidth, double viewportWidth)
    {
        if (viewportWidth <= 0 || itemWidth <= 0)
            return 0;

        double itemCenter = itemWidth / 2;
        double viewportCenter = viewportWidth / 2;
        double margin = viewportCenter - itemCenter;

        return Math.Max(0, margin);
    }

    /// <summary>
    /// Calculate the center positions of all carousel items
    /// </summary>
    /// <param name="itemCount">Number of items in the carousel</param>
    /// <param name="itemWidth">Width of each item</param>
    /// <param name="spacing">Spacing between items</param>
    /// <param name="margin">Left margin of the carousel container</param>
    /// <returns>List of center positions for each item</returns>
    public static List<double> CalculateItemCenters(
        int itemCount,
        double itemWidth,
        double spacing,
        double margin
    )
    {
        var centers = new List<double>();

        for (int i = 0; i < itemCount; i++)
        {
            double left = margin + i * (itemWidth + spacing);
            double center = left + (itemWidth / 2);
            centers.Add(center);
        }

        return centers;
    }

    #endregion

    #region Event Handlers

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ContainerWidth = e.NewSize.Width;
        UpdateCarouselLayout();
        CenterActiveElement(SelectedIndex);
    }

    private void OnPointerWheelChanged(object sender, PointerWheelEventArgs e)
    {
        // Block mouse wheel scrolling to prevent unwanted navigation
        e.Handled = true;
    }

    private void OnItemsSourceChanged(IEnumerable items)
    {
        if (_carouselItemsControl != null)
        {
            _carouselItemsControl.ItemsSource = items;
        }

        if (_indicatorsControl != null)
        {
            _indicatorsControl.ItemsSource = items;
        }

        UpdateCarouselLayout();
    }

    private void OnSelectedIndexChanged(int selectedIndex)
    {
        UpdateSlideIndicators(selectedIndex);
        CenterActiveElement(selectedIndex);

        if (ItemsSource != null)
        {
            var items = ItemsSource.Cast<object>().ToArray();
            if (
                selectedIndex >= 0
                && selectedIndex < items.Length
                && items[selectedIndex] is FeatureSlide slide
            )
            {
                MessageBus.Current.SendMessage(
                    new BackgroundColorChangedMessage(slide.BackgroundColor)
                );
            }
        }
    }

    private void OnCarouselPropertiesChanged()
    {
        UpdateCarouselLayout();
        CenterActiveElement(SelectedIndex);
    }

    #endregion

    #region Layout Management

    private void UpdateCarouselLayout()
    {
        if (_carouselItemsControl == null || _scrollViewer == null)
            return;

        // Get current container width
        double containerWidth = _scrollViewer.Bounds.Width;
        if (containerWidth <= 0)
            return;

        // Update container width property
        ContainerWidth = containerWidth;

        // Calculate symmetric margin
        double margin = CalculateSymmetricMargin(CardWidth, containerWidth);

        // Calculate item centers with the margin
        int itemCount = GetItemCount();
        _itemCenters = CalculateItemCenters(itemCount, CardWidth, CardSpacing, margin);

        // Apply symmetric margin to carousel items
        _carouselItemsControl.Margin = new Thickness(margin, 0, margin, 0);

        Console.WriteLine($"Container Width: {containerWidth}");
        Console.WriteLine($"Card Width: {CardWidth}");
        Console.WriteLine($"Card Spacing: {CardSpacing}");
        Console.WriteLine($"Calculated Margin: {margin}");
        Console.WriteLine($"Item Count: {itemCount}");
        Console.WriteLine(
            $"Item Centers: [{string.Join(", ", _itemCenters.Select(c => c.ToString("F1")))}]"
        );
    }

    private int GetItemCount()
    {
        return ItemsSource?.Cast<object>().Count() ?? 0;
    }

    #endregion

    #region Navigation and Animation

    private void CenterActiveElement(int selectedIndex)
    {
        if (_scrollViewer == null || _carouselItemsControl == null || _itemCenters == null)
            return;

        double containerWidth = _scrollViewer.Bounds.Width;
        if (containerWidth <= 0 || selectedIndex < 0 || selectedIndex >= _itemCenters.Count)
            return;

        double viewportCenter = containerWidth / 2;
        double center = _itemCenters[selectedIndex];

        double targetOffset = center - viewportCenter;

        double maxScrollExtent = _scrollViewer.Extent.Width - _scrollViewer.Viewport.Width;
        targetOffset = Math.Max(0, Math.Min(targetOffset, maxScrollExtent));

        _ = AnimateScrollTo(targetOffset);
    }

    private async Task AnimateScrollTo(double targetOffset)
    {
        if (_scrollViewer == null)
            return;

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(300),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Setters =
                    {
                        new Setter(
                            ScrollViewer.OffsetProperty,
                            new Vector(_scrollViewer.Offset.X, 0)
                        ),
                    },
                    Cue = new Cue(0),
                },
                new KeyFrame
                {
                    Setters =
                    {
                        new Setter(ScrollViewer.OffsetProperty, new Vector(targetOffset, 0)),
                    },
                    Cue = new Cue(1),
                },
            },
        };

        await animation.RunAsync(_scrollViewer);
    }

    #endregion

    #region Slide Indicators

    private void UpdateSlideIndicators(int selectedIndex)
    {
        if (_indicatorsControl == null || _indicatorsControl.ItemCount == 0)
            return;

        for (int i = 0; i < _indicatorsControl.ItemCount; i++)
        {
            if (
                _indicatorsControl.ContainerFromIndex(i) is ContentPresenter presenter
                && presenter.Child is Border indicator
            )
            {
                indicator.Classes.Clear();
                indicator.Classes.Add("carousel-indicator");

                if (i == selectedIndex)
                {
                    indicator.Classes.Add("selected");
                }
            }
        }
    }

    private void OnIndicatorTapped(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Border tappedIndicator)
        {
            // Find the index of the tapped indicator
            for (int i = 0; i < _indicatorsControl.ItemCount; i++)
            {
                if (
                    _indicatorsControl.ContainerFromIndex(i) is ContentPresenter presenter
                    && presenter.Child == tappedIndicator
                )
                {
                    SelectedIndex = i;
                    break;
                }
            }
        }
    }

    #endregion

    #region Template Management

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_carouselItemsControl != null)
        {
            _carouselItemsControl.ItemTemplate = CreateCarouselItemTemplate();
        }
    }

    private IDataTemplate CreateCarouselItemTemplate()
    {
        return new FuncDataTemplate<FeatureSlide>(
            (slide, _) =>
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.Parse(slide.BackgroundColor)),
                    CornerRadius = new CornerRadius(32),
                    Width = CardWidth,
                    Height = CardHeight,
                    BorderBrush = new SolidColorBrush(Color.Parse("#D2D3D2")),
                    BorderThickness = new Thickness(1),
                };

                var stackPanel = new StackPanel
                {
                    Spacing = 20,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Margin = new Thickness(24),
                };

                var icon = new PathIcon
                {
                    Data = Geometry.Parse(slide.IconPath),
                    Width = 48,
                    Height = 48,
                    Foreground = new SolidColorBrush(Color.Parse("#272320")),
                };

                var title = new TextBlock
                {
                    Text = slide.Title,
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#272320")),
                    TextAlignment = TextAlignment.Center,
                };

                var description = new TextBlock
                {
                    Text = slide.Description,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.Parse("#666666")),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20,
                };

                stackPanel.Children.Add(icon);
                stackPanel.Children.Add(title);
                stackPanel.Children.Add(description);
                border.Child = stackPanel;

                return border;
            }
        );
    }

    #endregion
}
