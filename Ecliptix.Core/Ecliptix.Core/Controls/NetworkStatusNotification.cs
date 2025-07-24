using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

public sealed class NetworkStatusNotification : UserControl, INotifyPropertyChanged
{
    public static readonly StyledProperty<string> StatusTextProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, string>(nameof(StatusText), "No Connection");

    public static readonly StyledProperty<IBrush> StatusBackgroundProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, IBrush>(nameof(StatusBackground),
            new SolidColorBrush(Color.FromRgb(43, 48, 51))); // Changed to #2b3033

    public static readonly StyledProperty<IBrush> EllipseColorProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, IBrush>(nameof(EllipseColor), Brushes.Transparent);

    public static readonly StyledProperty<string> IconPathProperty =
        AvaloniaProperty.Register<NetworkStatusNotification, string>(nameof(IconPath),
            "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2z");

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

    public string IconPath
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

    // Private fields
    private Border _mainBorder;
    private Ellipse _statusEllipse;
    private PathIcon _icon;
    private TextBlock _statusTextBlock;
    private Animation _flickerAnimation;
    private Animation _appearAnimation;
    private Animation _disappearAnimation;
    private Animation _ellipseColorTransition;
    private Animation _textTransition;

    public event PropertyChangedEventHandler PropertyChanged;

    public NetworkStatusNotification()
    {
        InitializeComponent();
        this.PropertyChanged += OnPropertyChanged;
        this.Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        CreateAnimations();
        UpdateBindings();
        this.Loaded -= OnLoaded;
    }

    private void InitializeComponent()
    {
        _mainBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        _icon = new PathIcon
        {
            Width = 20,
            Height = 20,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(_icon, 0);

        _statusTextBlock = new TextBlock
        {
            Foreground = Brushes.White,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap
        };
        Grid.SetColumn(_statusTextBlock, 1);

        _statusEllipse = new Ellipse
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, -2, -2, 0)
        };
        Grid.SetColumn(_statusEllipse, 2);

        grid.Children.Add(_icon);
        grid.Children.Add(_statusTextBlock);
        grid.Children.Add(_statusEllipse);

        _mainBorder.Child = grid;
        this.Content = _mainBorder;
    }

    private void UpdateBindings()
    {
        _mainBorder.Background = StatusBackground;
        _statusTextBlock.Text = StatusText;
        _statusEllipse.Fill = EllipseColor;

        if (!string.IsNullOrEmpty(IconPath))
        {
            try
            {
                _icon.Data = Geometry.Parse(IconPath);
            }
            catch
            {
                _icon.Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2z");
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
            IconPath = iconPath;
            try
            {
                _icon.Data = Geometry.Parse(IconPath);
            }
            catch
            {
                _icon.Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2z");
            }
        }

        _statusTextBlock.Text = StatusText;

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
            case nameof(StatusText):
            case nameof(StatusBackground):
            case nameof(EllipseColor):
            case nameof(IconPath):
                UpdateBindings();
                break;
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