using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace Ecliptix.Core.Controls;

public sealed partial class NetworkStatusNotification : UserControl, INotifyPropertyChanged
{
    public static readonly StyledProperty<string> StatusTextProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, string>(nameof(StatusText), "No Connection");

    public static readonly StyledProperty<IBrush> StatusBackgroundProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, IBrush>(nameof(StatusBackground),
            new SolidColorBrush(Color.FromRgb(43, 48, 51))); // Changed to #2b3033

    public static readonly StyledProperty<IBrush> EllipseColorProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, IBrush>(nameof(EllipseColor), Brushes.Transparent);

    public static readonly StyledProperty<Geometry> IconPathProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, Geometry>(nameof(IconPath),
            Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2z"));

    public static readonly StyledProperty<TimeSpan> AppearDurationProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, TimeSpan>(nameof(AppearDuration),
            TimeSpan.FromMilliseconds(300));

    public static readonly StyledProperty<TimeSpan> DisappearDurationProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, TimeSpan>(nameof(DisappearDuration),
            TimeSpan.FromMilliseconds(250));

    public static readonly StyledProperty<TimeSpan> FlickerDurationProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, TimeSpan>(nameof(FlickerDuration),
            TimeSpan.FromMilliseconds(1500));

    public string StatusText
    {
        get => GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public IBrush StatusBackground
    {
        get => GetValue(StatusBackgroundProperty);
        set => SetValue(StatusBackgroundProperty, value);
    }

    public IBrush EllipseColor
    {
        get => GetValue(EllipseColorProperty);
        set => SetValue(EllipseColorProperty, value);
    }

    public Geometry IconPath
    {
        get => GetValue(IconPathProperty);
        set => SetValue(IconPathProperty, value);
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

    // Private fields for animations
    private Animation _flickerAnimation;
    private Animation _appearAnimation;
    private Animation _disappearAnimation;
    private Animation _ellipseColorTransition;
    private Animation _textTransition;

    // XAML controls (these will be found by name)
    private Border _mainBorder;
    private Ellipse _statusEllipse;
    private PathIcon _statusIcon;
    private TextBlock _statusTextBlock;

    public event PropertyChangedEventHandler PropertyChanged;

    public NetworkStatusNotification()
    {
        InitializeComponent();
        this.PropertyChanged += OnPropertyChanged;
        this.Loaded += OnLoaded;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        
        // Find controls by name
        _mainBorder = this.FindControl<Border>("MainBorder");
        _statusEllipse = this.FindControl<Ellipse>("StatusEllipse");
        _statusIcon = this.FindControl<PathIcon>("StatusIcon");
        _statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock");
    }

    private void OnLoaded(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        CreateAnimations();
        this.Loaded -= OnLoaded;
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

        _ellipseColorTransition = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(600),
            Easing = new QuadraticEaseInOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(Ellipse.FillProperty, _statusEllipse?.Fill ?? Brushes.Transparent) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(Ellipse.FillProperty, Brushes.Transparent) }
                }
            }
        };

        _textTransition = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(300),
            Easing = new QuadraticEaseInOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(OpacityProperty, 1d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.5d),
                    Setters = { new Setter(OpacityProperty, 0.6d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(OpacityProperty, 1d) }
                }
            }
        };
    }

    public async Task ShowAsync()
    {
        if (_appearAnimation == null)
            CreateAnimations();

        this.IsVisible = true;
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
    }

    public void StartFlickerAnimation()
    {
        if (_flickerAnimation == null)
            CreateAnimations();

        _flickerAnimation?.RunAsync(_statusEllipse);
    }

    public void StopFlickerAnimation()
    {
        _statusEllipse.Opacity = 1.0;
    }

    public async Task UpdateStatusWithAnimation(string status, IBrush ellipseColor, string iconPath = null)
    {
        StopFlickerAnimation();

        StatusText = status;
        if (!string.IsNullOrEmpty(iconPath))
        {
            try
            {
                IconPath = Geometry.Parse(iconPath);
            }
            catch
            {
                IconPath = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2z");
            }
        }

        await UpdateEllipseColorAsync(ellipseColor);
        await AnimateTextTransitionAsync();

        StatusBackground = new SolidColorBrush(Color.FromRgb(43, 48, 51)); // Set to #2b3033
        EllipseColor = ellipseColor;
        StartFlickerAnimation();
    }

    private async Task UpdateEllipseColorAsync(IBrush newColor)
    {
        var endKeyFrame = _ellipseColorTransition.Children[1] as KeyFrame;
        if (endKeyFrame != null)
        {
            endKeyFrame.Setters[0] = new Setter(Ellipse.FillProperty, newColor);
        }

        await _ellipseColorTransition.RunAsync(_statusEllipse);
    }

    private async Task AnimateTextTransitionAsync()
    {
        await _textTransition.RunAsync(_statusTextBlock);
    }

    private void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppearDuration):
            case nameof(DisappearDuration):
            case nameof(FlickerDuration):
                CreateAnimations();
                break;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}