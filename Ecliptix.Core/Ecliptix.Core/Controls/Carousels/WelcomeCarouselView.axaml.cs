using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Ecliptix.Core.Controls.Carousels;

public record WelcomeSlideItemTemplate(string Title, string Description, Bitmap? Image);

public partial class WelcomeCarouselView : UserControl
{
    private const string INDICATORS_CONTROL_NAME = "IndicatorsControl";
    private const string ACTIVE_CLASS = "active";

    private ItemsControl? _indicators;

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<WelcomeCarouselView, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<WelcomeCarouselView, int>(nameof(SelectedIndex));

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    static WelcomeCarouselView()
    {
        ItemsSourceProperty.Changed.AddClassHandler<WelcomeCarouselView>((x, e) =>
            x.OnItemsSourceChanged(e.NewValue as IEnumerable));
        SelectedIndexProperty.Changed.AddClassHandler<WelcomeCarouselView>((x, e) =>
            x.OnSelectedIndexChanged(e.NewValue is int i ? i : 0));
    }

    public WelcomeCarouselView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _indicators = this.FindControl<ItemsControl>(INDICATORS_CONTROL_NAME);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => UpdateIndicators(SelectedIndex), DispatcherPriority.Loaded);
    }

    private void OnItemsSourceChanged(IEnumerable? items)
    {
        _indicators?.ItemsSource = items;

        UpdateIndicators(SelectedIndex);
    }

    private void OnSelectedIndexChanged(int index) => UpdateIndicators(index);

    private void UpdateIndicators(int activeIndex)
    {
        if (_indicators == null)
        {
            return;
        }

        int index = 0;
        foreach (Control item in _indicators.GetRealizedContainers())
        {
            if (item is ContentPresenter { Child: Border { Child: Border dot } })
            {
                if (index == activeIndex)
                {
                    if (!dot.Classes.Contains(ACTIVE_CLASS))
                    {
                        dot.Classes.Add(ACTIVE_CLASS);
                    }
                }
                else
                {
                    dot.Classes.Remove(ACTIVE_CLASS);
                }
            }

            index++;
        }
    }

    private void OnIndicatorPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.DataContext == null)
        {
            return;
        }

        if (ItemsSource is not IList list)
        {
            return;
        }

        int index = list.IndexOf(border.DataContext);
        if (index >= 0)
        {
            SelectedIndex = index;
        }
    }
}
