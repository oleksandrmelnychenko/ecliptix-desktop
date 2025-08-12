using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Avalonia.Styling;
using Ecliptix.Core.Services;
using ReactiveUI;

namespace Ecliptix.Core.Controls;

public sealed partial class NetworkStatusNotification : ReactiveUserControl<NetworkStatusNotificationViewModel>
{
    private Animation? _flickerAnimation;
    private Animation? _appearAnimation;
    private Animation? _disappearAnimation;

    private Ellipse? _statusEllipse;

    private bool _isVisible;

    private const string DisconnectedIconPath =
        "M8 15A7 7 0 1 1 8 1a7 7 0 0 1 0 14zm0 1A8 8 0 1 0 8 0a8 8 0 0 0 0 16z " +
        "m.93-9.412-2.29.287-.082.38.45.083c.294.07.352.176.288.469l-.738 3.468c-.194.897.105 1.319.808 1.319.545 0 1.178-.252 1.465-.598l.088-.416c-.2.176-.492.246-.686.246-.275 0-.375-.193-.304-.533L8.93 6.588zM9 4.5a1 1 0 1 1-2 0 1 1 0 0 1 2 0z";

    public static readonly StyledProperty<TimeSpan> AppearDurationProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, TimeSpan>(nameof(AppearDuration),
            TimeSpan.FromMilliseconds(300));

    public static readonly StyledProperty<TimeSpan> DisappearDurationProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, TimeSpan>(nameof(DisappearDuration),
            TimeSpan.FromMilliseconds(250));

    public static readonly StyledProperty<TimeSpan> FlickerDurationProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, TimeSpan>(nameof(FlickerDuration),
            TimeSpan.FromMilliseconds(1500));

    public new static readonly StyledProperty<IBrush> BackgroundProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, IBrush>(nameof(Background),
            new SolidColorBrush(Color.Parse("#2f2f2f")));

    public static readonly StyledProperty<IBrush> EllipseColorProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, IBrush>(nameof(EllipseColor),
            new SolidColorBrush(Color.Parse("#d81c1c")));

    public static readonly StyledProperty<Geometry> IconDataProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, Geometry>(nameof(IconData));

    public static readonly StyledProperty<ILocalizationService> LocalizationServiceProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, ILocalizationService>(nameof(LocalizationService));

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

    public TimeSpan FlickerDuration
    {
        get => GetValue(FlickerDurationProperty);
        set => SetValue(FlickerDurationProperty, value);
    }
    

    public NetworkStatusNotification()
    {
        InitializeComponent();

        IsVisible = false;

        SetIcon();
        
    }

    private void SetIcon()
    {
        try
        {
            IconData = Geometry.Parse(DisconnectedIconPath);
        }
        catch
        {
            IconData = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2z");
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _statusEllipse = this.FindControl<Ellipse>("StatusEllipse");
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        CreateAnimations();
    }

    private async Task HandleConnectivityChange(bool isConnected)
    {
        if (!isConnected)
        {
            if (!_isVisible)
            {
                await ShowAsync();
            }
        }
        else
        {
            if (_isVisible)
            {
                await HideAsync();
            }
        }
    }

    private void CreateAnimations()
    {
        _appearAnimation = new Animation
        {
            Duration = AppearDuration,
            Easing = new QuadraticEaseOut(),
            FillMode = FillMode.Both,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0d),
                        new Setter(TranslateTransform.YProperty, -20d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(OpacityProperty, 1d),
                        new Setter(TranslateTransform.YProperty, 0d)
                    }
                }
            }
        };

        _disappearAnimation = new Animation
        {
            Duration = DisappearDuration,
            Easing = new QuadraticEaseIn(),
            FillMode = FillMode.Both,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(OpacityProperty, 1d),
                        new Setter(TranslateTransform.YProperty, 0d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0d),
                        new Setter(TranslateTransform.YProperty, -15d)
                    }
                }
            }
        };

        _flickerAnimation = new Animation
        {
            Duration = FlickerDuration,
            IterationCount = IterationCount.Infinite,
            Easing = new SineEaseInOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(OpacityProperty, 0.3d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.5d),
                    Setters = { new Setter(OpacityProperty, 1d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(OpacityProperty, 0.3d) }
                }
            }
        };
    }

    private async Task ShowAsync()
    {
        if (_appearAnimation == null)
        {
            CreateAnimations();
        }

        IsVisible = true;
        _isVisible = true;
        RenderTransform = new TranslateTransform();
        await _appearAnimation!.RunAsync(this);
        StartFlickerAnimation();
    }

    private async Task HideAsync()
    {
        if (_disappearAnimation == null)
        {
            CreateAnimations();
        }

        StopFlickerAnimation();
        await _disappearAnimation!.RunAsync(this);
        IsVisible = false;
        _isVisible = false;
    }

    private void StartFlickerAnimation()
    {
        if (_flickerAnimation == null)
        {
            CreateAnimations();
        }

        _flickerAnimation?.RunAsync(_statusEllipse!);
    }

    private void StopFlickerAnimation()
    {
        if (_statusEllipse != null)
        {
            _statusEllipse.Opacity = 1.0;
        }
    }
}