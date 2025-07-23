namespace Ecliptix.Core.Controls;

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

    public class NetworkStatusNotification : UserControl, INotifyPropertyChanged
    {
        // Styled properties
        public static readonly StyledProperty<string> StatusTextProperty =
            AvaloniaProperty.Register<NetworkStatusNotification, string>(nameof(StatusText), "No Connection");

        public static readonly StyledProperty<IBrush> StatusBackgroundProperty =
            AvaloniaProperty.Register<NetworkStatusNotification, IBrush>(nameof(StatusBackground), 
                new SolidColorBrush(Color.FromRgb(0x5b, 0x5e, 0x63))); // Default background #5b5e63

        public static readonly StyledProperty<IBrush> EllipseColorProperty =
            AvaloniaProperty.Register<NetworkStatusNotification, IBrush>(nameof(EllipseColor), Brushes.Transparent);

        public static readonly StyledProperty<string> IconPathProperty =
            AvaloniaProperty.Register<NetworkStatusNotification, string>(nameof(IconPath), "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2z");

        public static readonly StyledProperty<TimeSpan> AppearDurationProperty =
            AvaloniaProperty.Register<NetworkStatusNotification, TimeSpan>(nameof(AppearDuration), TimeSpan.FromMilliseconds(300));

        public static readonly StyledProperty<TimeSpan> DisappearDurationProperty =
            AvaloniaProperty.Register<NetworkStatusNotification, TimeSpan>(nameof(DisappearDuration), TimeSpan.FromMilliseconds(250));

        public static readonly StyledProperty<TimeSpan> FlickerDurationProperty =
            AvaloniaProperty.Register<NetworkStatusNotification, TimeSpan>(nameof(FlickerDuration), TimeSpan.FromMilliseconds(1500));

        // Properties
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
            // Create the main container
            _mainBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            // Create a grid for layout
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            // Create icon
            _icon = new PathIcon
            {
                Width = 20,
                Height = 20,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(_icon, 0);

            // Create status text
            _statusTextBlock = new TextBlock
            {
                Foreground = Brushes.White,
                FontWeight = FontWeight.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap
            };
            Grid.SetColumn(_statusTextBlock, 1);

            // Create status ellipse
            _statusEllipse = new Ellipse
            {
                Width = 8,
                Height = 8,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, -2, -2, 0)
            };
            Grid.SetColumn(_statusEllipse, 2);

            // Add controls to grid
            grid.Children.Add(_icon);
            grid.Children.Add(_statusTextBlock);
            grid.Children.Add(_statusEllipse);

            _mainBorder.Child = grid;
            this.Content = _mainBorder;
        }

        private void UpdateBindings()
        {
            if (_mainBorder == null || _statusTextBlock == null || _statusEllipse == null || _icon == null)
                return;

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
                    // Fallback to default icon if parsing fails
                    _icon.Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2z");
                }
            }
        }

        private void CreateAnimations()
        {
            // Simple, smooth appear animation - just fade in
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

            // Simple, smooth disappear animation - just fade out
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

            // Smooth flicker animation for ellipse
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

            // Reusable smooth ellipse color transition
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
                        Setters = { new Setter(Ellipse.FillProperty, Brushes.Transparent) } // Will be updated dynamically
                    }
                }
            };

            // Reusable text fade transition
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
            // Stop flicker during transition
            StopFlickerAnimation();

            // Background stays the same (#5b5e63), so no background transition needed
            var backgroundColor = new SolidColorBrush(Color.FromRgb(0x5b, 0x5e, 0x63));

            // Update text and icon immediately
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

            // Use reusable animations with dynamic color updates
            await UpdateEllipseColorAsync(ellipseColor);
            await AnimateTextTransitionAsync();

            // Update final properties and restart flicker
            StatusBackground = backgroundColor;
            EllipseColor = ellipseColor;
            StartFlickerAnimation();
        }

        private async Task UpdateEllipseColorAsync(IBrush newColor)
        {
            // Dynamically update the end color in the existing animation
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

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    
    
    public class NetworkNotificationManager
    {
        private NetworkStatusNotification _currentNotification;
        private Panel _parentContainer;
        
        // Consistent background color for all notifications
        private readonly IBrush _notificationBackground = new SolidColorBrush(Color.FromRgb(0x5b, 0x5e, 0x63));

        public NetworkNotificationManager(Panel parentContainer)
        {
            _parentContainer = parentContainer;
        }

        public async Task ShowNetworkStatus(NetworkStatus status)
        {
            if (_currentNotification == null)
            {
                // Create new notification for first time
                _currentNotification = new NetworkStatusNotification();
                _parentContainer.Children.Add(_currentNotification);
                
                SetNotificationContent(status);
                await _currentNotification.ShowAsync();
            }
            else
            {
                // Update existing notification with animation
                await UpdateExistingNotification(status);
            }
        }

        private void SetNotificationContent(NetworkStatus status)
        {
            var (statusText, ellipseColor, iconPath) = GetStatusConfiguration(status);
            
            _currentNotification.StatusText = statusText;
            _currentNotification.StatusBackground = _notificationBackground;
            _currentNotification.EllipseColor = ellipseColor;
            _currentNotification.IconPath = iconPath;
        }

        private async Task UpdateExistingNotification(NetworkStatus status)
        {
            var (statusText, ellipseColor, iconPath) = GetStatusConfiguration(status);
            await _currentNotification.UpdateStatusWithAnimation(statusText, ellipseColor, iconPath);
        }

        private (string statusText, IBrush ellipseColor, string iconPath) GetStatusConfiguration(NetworkStatus status)
        {
            return status switch
            {
                NetworkStatus.DataCenterConnected => (
                    "Data Center Connected",
                    new SolidColorBrush(Color.FromRgb(108, 217, 134)), // Light green
                    "M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" // Check circle icon
                ),
                
                NetworkStatus.DataCenterDisconnected => (
                    "Data Center Disconnected", 
                    new SolidColorBrush(Color.FromRgb(255, 107, 107)), // Light red
                    "M10 14l2-2m0 0l2-2m-2 2l-2-2m2 2l2 2m7-2a9 9 0 11-18 0 9 9 0 0118 0z" // X circle icon
                ),
                
                NetworkStatus.DataCenterConnecting => (
                    "Connecting to Data Center",
                    new SolidColorBrush(Color.FromRgb(255, 235, 59)), // Light yellow
                    "M12 6v6l4 2" // Clock icon (connecting/waiting)
                ),
                
                NetworkStatus.RestoreSecrecyChannel => (
                    "Restoring Secrecy Channel",
                    new SolidColorBrush(Color.FromRgb(173, 216, 230)), // Light blue
                    "M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" // Lock icon
                ),
                
                _ => (
                    "Unknown Status",
                    new SolidColorBrush(Color.FromRgb(169, 169, 169)), // Gray
                    "M8.228 9c.549-1.165 2.03-2 3.772-2 2.21 0 4 1.343 4 3 0 1.4-1.278 2.575-3.006 2.907-.542.104-.994.54-.994 1.093m0 3h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" // Question mark icon
                )
            };
        }

        public async Task HideCurrentNotification()
        {
            if (_currentNotification != null)
            {
                await _currentNotification.HideAsync();
                _parentContainer.Children.Remove(_currentNotification);
                _currentNotification = null;
            }
        }
    }

    // Your custom NetworkStatus enum
    public enum NetworkStatus
    {
        DataCenterConnected,
        DataCenterDisconnected,
        DataCenterConnecting,
        RestoreSecrecyChannel,
    }
