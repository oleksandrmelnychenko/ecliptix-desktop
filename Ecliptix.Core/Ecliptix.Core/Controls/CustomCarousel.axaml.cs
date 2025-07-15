using System;
using System.Collections;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
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
using Ecliptix.Core.ViewModels.Memberships;
using ReactiveUI;

namespace Ecliptix.Core.Controls;

public partial class CustomCarousel : UserControl
{
    private ItemsControl _indicatorsControl;
    private ScrollViewer _scrollViewer;
    private ItemsControl _carouselItemsControl;

    public static readonly StyledProperty<IEnumerable> ItemsSourceProperty =
        AvaloniaProperty.Register<CustomCarousel, IEnumerable>(nameof(ItemsSource));

    public static readonly StyledProperty<int> SelectedIndexProperty = AvaloniaProperty.Register<
        CustomCarousel,
        int
    >(nameof(SelectedIndex));

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

    public static readonly StyledProperty<double> CardWidthProperty = AvaloniaProperty.Register<
        CustomCarousel,
        double
    >(nameof(CardWidth), 240);

    public static readonly StyledProperty<double> CardHeightProperty = AvaloniaProperty.Register<
        CustomCarousel,
        double
    >(nameof(CardHeight), 280);

    public static readonly StyledProperty<double> CardSpacingProperty = AvaloniaProperty.Register<
        CustomCarousel,
        double
    >(nameof(CardSpacing), 24);

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

    public CustomCarousel()
    {
        AvaloniaXamlLoader.Load(this);

        _indicatorsControl = this.FindControl<ItemsControl>("SlideIndicators");
        _scrollViewer = this.FindControl<ScrollViewer>("CarouselScrollViewer");
        _carouselItemsControl = this.FindControl<ItemsControl>("CarouselItemsControl");

        // Block mouse scroll
        _scrollViewer.PointerWheelChanged += OnPointerWheelChanged;

        // Subscribe to property changes
        this.GetObservable(ItemsSourceProperty).Subscribe(OnItemsSourceChanged);

        this.GetObservable(SelectedIndexProperty).Subscribe(OnSelectedIndexChanged);

        // Handle size changes to update margins
        this.SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCarouselMargins();
        // Re-center the current element after size change
        CenterActiveElement(SelectedIndex);
    }

    private void OnPointerWheelChanged(object sender, PointerWheelEventArgs e)
    {
        // Block mouse wheel scrolling
        e.Handled = true;
    }

    private void OnItemsSourceChanged(IEnumerable items)
    {
        if (_carouselItemsControl != null)
        {
            _carouselItemsControl.ItemsSource = items;

            // Calculate and set dynamic margin for proper centering
            if (items != null)
            {
                UpdateCarouselMargins();
            }
        }
        if (_indicatorsControl != null)
        {
            _indicatorsControl.ItemsSource = items;
        }
    }

    private void UpdateCarouselMargins()
    {
        if (_carouselItemsControl == null || _scrollViewer == null)
            return;

        // Calculate padding needed to center the first and last elements
        double containerWidth = _scrollViewer.Bounds.Width;
        if (containerWidth <= 0)
            return;

        double padding = (containerWidth - CardWidth) / 4;
        // 5.0769230769
        // Find the items panel and update its margin
        if (_carouselItemsControl.ItemsPanel != null)
        {
            // We need to modify the XAML to apply margin dynamically
            // For now, let's set a computed margin through code
            _carouselItemsControl.Margin = new Thickness(padding, 0, padding, 0);
        }
    }

    private void OnSelectedIndexChanged(int selectedIndex)
    {
        UpdateSlideIndicators(selectedIndex);
        CenterActiveElement(selectedIndex);
    }

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
                indicator.Background =
                    i == selectedIndex
                        ? new SolidColorBrush(Color.Parse("#272320"))
                        : new SolidColorBrush(Color.Parse("#D0D0D0"));
            }
        }
    }

    private void CenterActiveElement(int selectedIndex)
    {
        if (_scrollViewer == null || _carouselItemsControl == null)
            return;

        // Calculate the position to center the active element
        double slideWidth = CardWidth + CardSpacing;
        double containerWidth = _scrollViewer.Bounds.Width;

        if (containerWidth <= 0)
            return;

        // The margin we use for the carousel items
        double padding = (containerWidth - CardWidth) / 2;

        // Calculate the target scroll position
        double targetOffset = (selectedIndex * slideWidth) - padding;

        // Get the maximum scrollable extent
        double maxScrollExtent = _scrollViewer.Extent.Width - _scrollViewer.Viewport.Width;

        // Clamp the target offset to valid range
        targetOffset = Math.Max(0, Math.Min(targetOffset, maxScrollExtent));

        // Animate to the target offset
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
                    // Update the selected index
                    SelectedIndex = i;
                    break;
                }
            }
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // Apply the custom data template for carousel items
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
                    Width = 240,
                    Height = 280,
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
}
