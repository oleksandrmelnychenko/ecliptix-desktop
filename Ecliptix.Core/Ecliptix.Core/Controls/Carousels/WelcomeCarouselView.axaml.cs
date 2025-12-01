using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Ecliptix.Core.Controls.Carousels;



public class WelcomeSlide

{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Bitmap? Image { get; set; } = null;

}



public partial class WelcomeCarouselView : UserControl

{
    private Carousel _carousel;
    private ItemsControl _indicators;

    #region Styled Properties

    public static readonly StyledProperty<IEnumerable> ItemsSourceProperty =
        AvaloniaProperty.Register<WelcomeCarouselView, IEnumerable>(nameof(ItemsSource));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<WelcomeCarouselView, int>(nameof(SelectedIndex));

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
    #endregion

    public WelcomeCarouselView()
    {
        InitializeComponent();
    }
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _carousel = this.FindControl<Carousel>("MainCarousel");
        _indicators = this.FindControl<ItemsControl>("IndicatorsControl");

        this.GetObservable(ItemsSourceProperty).Subscribe(OnItemsSourceChanged);
        this.GetObservable(SelectedIndexProperty).Subscribe(OnSelectedIndexChanged);

    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => UpdateIndicators(SelectedIndex), DispatcherPriority.Loaded);

    }

    private void OnItemsSourceChanged(IEnumerable items)

    {


        if (_indicators != null)
        {
            _indicators.ItemsSource = items;
        }

        UpdateIndicators(SelectedIndex);

    }



    private void OnSelectedIndexChanged(int index)
    {
        UpdateIndicators(index);
    }

    private void UpdateIndicators(int activeIndex)
    {
        if (_indicators == null)
        {
            return;
        }

        int index = 0;

        foreach (Control item in _indicators.GetRealizedContainers())
        {
            if (item is ContentPresenter cp && cp.Child is Border outerBorder && outerBorder.Child is Border dot)
            {
                if (index == activeIndex)
                {
                    if (!dot.Classes.Contains("active"))
                    {
                        dot.Classes.Add("active");
                    }
                }
                else
                {
                    dot.Classes.Remove("active");
                }
            }
            index++;
        }
    }

    private void OnIndicatorPressed(object sender, PointerPressedEventArgs e)
    {

        if (sender is Border border && border.DataContext != null)
        {

            IList? list = ItemsSource as IList;

            if (list != null)
            {
                int index = list.IndexOf(border.DataContext);
                if (index >= 0)
                {
                    SelectedIndex = index;
                }
            }
        }
    }
}

