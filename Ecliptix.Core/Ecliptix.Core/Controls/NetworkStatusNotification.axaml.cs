using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace Ecliptix.Core.Controls;

public sealed partial class NetworkStatusNotification : UserControl, INotifyPropertyChanged
{
    #region properties
    public static readonly StyledProperty<bool> IsConnectedProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, bool>(nameof(IsConnected), true);

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
            new SolidColorBrush(Color.Parse("#312e31")));

    public static readonly StyledProperty<IBrush> EllipseColorProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, IBrush>(nameof(EllipseColor),
            new SolidColorBrush(Color.FromRgb(255, 107, 107)));

    public static readonly StyledProperty<Geometry> IconDataProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, Geometry>(nameof(IconData));

    #endregion 
    
    #region fields
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

    public bool IsConnected
    {
        get => GetValue(IsConnectedProperty);
        set => SetValue(IsConnectedProperty, value);
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
    
    #endregion
    
    private Animation _flickerAnimation;
    private Animation _appearAnimation;
    private Animation _disappearAnimation;
    
    private Ellipse _statusEllipse;

    private bool _isVisible;
    
    private const string DisconnectedIconPath =
        "M10.8809 16.15C10.8809 16.0021 10.9101 15.8556 10.967 15.7191C11.024 15.5825 11.1073 15.4586 11.2124 15.3545C11.3175 15.2504 11.4422 15.1681 11.5792 15.1124C11.7163 15.0567 11.8629 15.0287 12.0109 15.03C12.2291 15.034 12.4413 15.1021 12.621 15.226C12.8006 15.3499 12.9399 15.5241 13.0211 15.7266C13.1024 15.9292 13.122 15.1512 13.0778 15.3649C13.0335 16.5786 12.9272 16.7745 12.7722 16.9282C12.6172 17.0818 12.4204 17.1863 12.2063 17.2287C11.9922 17.2711 11.7703 17.2494 11.5685 17.1663C11.3666 17.0833 11.1938 16.9426 11.0715 16.7618C10.9492 16.5811 10.8829 16.3683 10.8809 16.15ZM11.2408 13.42L11.1008 8.20001C11.0875 8.07453 11.1008 7.94766 11.1398 7.82764C11.1787 7.70761 11.2424 7.5971 11.3268 7.5033C11.4112 7.40949 11.5144 7.33449 11.6296 7.28314C11.7449 7.2318 11.8697 7.20526 11.9958 7.20526C12.122 7.20526 12.2468 7.2318 12.3621 7.28314C12.4773 7.33449 12.5805 7.40949 12.6649 7.5033C12.7493 7.5971 12.813 7.70761 12.8519 7.82764C12.8909 7.94766 12.9042 8.07453 12.8909 8.20001L12.7609 13.42C12.7609 13.6215 12.6809 13.8149 12.5383 13.9574C12.3958 14.0999 12.2024 14.18 12.0009 14.18C11.7993 14.18 11.606 14.0999 11.4635 13.9574C11.321 13.8149 11.2408 13.6215 11.2408 13.42Z M12 21.5C17.1086 21.5 21.25 17.3586 21.25 12.25C21.25 7.14137 17.1086 3 12 3C6.89137 3 2.75 7.14137 2.75 12.25C2.75 17.3586 6.89137 21.5 12 21.5Z";
    

    public NetworkStatusNotification()
    {
        InitializeComponent();
        
        IsConnectedProperty.Changed.Subscribe(OnIsConnectedChanged);
        
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

    
    private async void OnIsConnectedChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is bool isConnected)
        {
            await HandleConnectivityChange(isConnected);
        }
    }

    private async Task HandleConnectivityChange(bool isConnected)
    {
        if (!isConnected)
        {
            // Show notification when disconnected
            if (!_isVisible)
            {
                await ShowAsync();
            }
        }
        else
        {
            // Hide notification when connected
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
                    Setters = { new Setter(OpacityProperty, 0.4d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.5d),
                    Setters = { new Setter(OpacityProperty, 1d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(OpacityProperty, 0.4d) }
                }
            }
        };
    }

    public async Task ShowAsync()
    {
        if (_appearAnimation == null)
            CreateAnimations();

        this.IsVisible = true;
        _isVisible = true;
        this.RenderTransform = new TranslateTransform();
        await _appearAnimation.RunAsync(this);
        StartFlickerAnimation();
    }

    public async Task HideAsync()
    {
        if (_disappearAnimation == null)
            CreateAnimations();

        StopFlickerAnimation();
        await _disappearAnimation.RunAsync(this);
        this.IsVisible = false;
        _isVisible = false;
    }

    public void StartFlickerAnimation()
    {
        if (_flickerAnimation == null)
            CreateAnimations();

        _flickerAnimation?.RunAsync(_statusEllipse);
    }

    public void StopFlickerAnimation()
    {
        if (_statusEllipse != null)
            _statusEllipse.Opacity = 1.0;
    }
    
}