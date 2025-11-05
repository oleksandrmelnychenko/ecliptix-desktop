using System;
using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Services.Abstractions.Core;

namespace Ecliptix.Core.Controls.Core;

public sealed partial class ConnectivityNotificationView : ReactiveUserControl<ConnectivityNotificationViewModel>
{
    private const string DISCONNECTED_ICON_PATH =
        "M8 15A7 7 0 1 1 8 1a7 7 0 0 1 0 14zm0 1A8 8 0 1 0 8 0a8 8 0 0 0 0 16z " +
        "m.93-9.412-2.29.287-.082.38.45.083c.294.07.352.176.288.469l-.738 3.468c-.194.897.105 1.319.808 1.319.545 0 1.178-.252 1.465-.598l.088-.416c-.2.176-.492.246-.686.246-.275 0-.375-.193-.304-.533L8.93 6.588zM9 4.5a1 1 0 1 1-2 0 1 1 0 0 1 2 0z";

    public static readonly StyledProperty<TimeSpan> AppearDurationProperty =
        AvaloniaProperty.Register<ConnectivityNotificationView, TimeSpan>(nameof(AppearDuration),
            TimeSpan.FromMilliseconds(300));

    public static readonly StyledProperty<TimeSpan> DisappearDurationProperty =
        AvaloniaProperty.Register<ConnectivityNotificationView, TimeSpan>(nameof(DisappearDuration),
            TimeSpan.FromMilliseconds(250));
    public new static readonly StyledProperty<IBrush> BackgroundProperty =
        AvaloniaProperty.Register<ConnectivityNotificationView, IBrush>(nameof(Background),
            new SolidColorBrush(Color.Parse("#2f2f2f")));

    public static readonly StyledProperty<IBrush> EllipseColorProperty =
        AvaloniaProperty.Register<ConnectivityNotificationView, IBrush>(nameof(EllipseColor),
            new SolidColorBrush(Color.Parse("#d81c1c")));

    public static readonly StyledProperty<Geometry> IconDataProperty =
        AvaloniaProperty.Register<ConnectivityNotificationView, Geometry>(nameof(IconData));

    public static readonly StyledProperty<ILocalizationService> LocalizationServiceProperty =
        AvaloniaProperty.Register<ConnectivityNotificationView, ILocalizationService>(nameof(LocalizationService));

    public ILocalizationService LocalizationService
    {
        get => GetValue(LocalizationServiceProperty);
        set => SetValue(LocalizationServiceProperty, value);
    }

    public new IBrush Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public IBrush EllipseColor
    {
        get => GetValue(EllipseColorProperty);
        set => SetValue(EllipseColorProperty, value);
    }

    public Geometry IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public TimeSpan AppearDuration
    {
        get => GetValue(AppearDurationProperty);
        set => SetValue(AppearDurationProperty, value);
    }

    public TimeSpan DisappearDuration
    {
        get => GetValue(DisappearDurationProperty);
        set => SetValue(DisappearDurationProperty, value);
    }
    public ConnectivityNotificationView()
    {
        InitializeComponent();
        IsVisible = false;
        SetIcon();
    }

    private void SetIcon()
    {
        try
        {
            IconData = Geometry.Parse(DISCONNECTED_ICON_PATH);
        }
        catch
        {
            IconData = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2z");
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is not ConnectivityNotificationViewModel viewModel)
        {
            return;
        }

        viewModel.SetView(this);

        viewModel.AppearDuration = AppearDuration;
        viewModel.DisappearDuration = DisappearDuration;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (DataContext is ConnectivityNotificationViewModel viewModel)
        {
            if (change.Property == AppearDurationProperty)
            {
                viewModel.AppearDuration = AppearDuration;
            }
            else if (change.Property == DisappearDurationProperty)
            {
                viewModel.DisappearDuration = DisappearDuration;
            }
        }
    }
}
