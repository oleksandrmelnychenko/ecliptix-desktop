using System;
using System.Collections;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Ecliptix.Core.Controls.Illustrations;
using Ecliptix.Core.Shell.Abstractions.Core;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Carousels;

public class WelcomeSlideItemTemplate : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public WelcomeSlideItemTemplate(string titleKey, string descriptionKey, ILocalizationService localizationService)
    {

        Observable.FromEvent(
                handler => localizationService.LanguageChanged += handler,
                handler => localizationService.LanguageChanged -= handler
            )
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                Title = localizationService[titleKey];
                Description = localizationService[descriptionKey];
            })
            .DisposeWith(_disposables);

        Title = localizationService[titleKey];
        Description = localizationService[descriptionKey];
    }

    public string Title
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string Description
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public void Dispose() => _disposables.Dispose();
}

public partial class WelcomeCarouselView : UserControl
{
    private const string INDICATORS_CONTROL_NAME = "IndicatorsControl";
    private const string HTML_ILLUSTRATION_NAME = "HtmlIllustration";
    private const string ACTIVE_CLASS = "active";

    private ItemsControl? _indicators;
    private HtmlIllustrationView? _htmlIllustration;

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<WelcomeCarouselView, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<WelcomeCarouselView, int>(nameof(SelectedIndex));

    public static readonly StyledProperty<string?> HtmlUrlProperty =
        AvaloniaProperty.Register<WelcomeCarouselView, string?>(nameof(HtmlUrl));

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

    public string? HtmlUrl
    {
        get => GetValue(HtmlUrlProperty);
        set => SetValue(HtmlUrlProperty, value);
    }

    static WelcomeCarouselView()
    {
        ItemsSourceProperty.Changed.AddClassHandler<WelcomeCarouselView>((x, e) =>
            x.OnItemsSourceChanged(e.NewValue as IEnumerable));
        SelectedIndexProperty.Changed.AddClassHandler<WelcomeCarouselView>((x, e) =>
            x.OnSelectedIndexChanged(e.NewValue is int i ? i : 0));
        HtmlUrlProperty.Changed.AddClassHandler<WelcomeCarouselView>((x, e) =>
            x.OnHtmlUrlChanged(e.NewValue as string));
    }

    public WelcomeCarouselView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _indicators = this.FindControl<ItemsControl>(INDICATORS_CONTROL_NAME);
        _htmlIllustration = this.FindControl<HtmlIllustrationView>(HTML_ILLUSTRATION_NAME);
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

    private void OnHtmlUrlChanged(string? htmlUrl)
    {
        if (_htmlIllustration != null && !string.IsNullOrEmpty(htmlUrl))
        {
            _htmlIllustration.Url = htmlUrl;
        }
    }
}
