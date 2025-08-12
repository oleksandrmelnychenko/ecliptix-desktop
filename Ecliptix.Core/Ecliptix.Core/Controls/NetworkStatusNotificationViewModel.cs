using System;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.Services;
using ReactiveUI;

namespace Ecliptix.Core.Controls;

public sealed class NetworkStatusNotificationViewModel : ReactiveObject
{
    public ILocalizationService LocalizationService { get; }
    
    private readonly INetworkEvents _networkEvents;
    private NetworkStatusNotification? _view;
    private Ellipse? _statusEllipse;
    
    // Animation objects
    private Animation? _flickerAnimation;
    private Animation? _appearAnimation;
    private Animation? _disappearAnimation;
    
    // Animation durations
    public TimeSpan AppearDuration { get; set; } = TimeSpan.FromMilliseconds(300);
    public TimeSpan DisappearDuration { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan FlickerDuration { get; set; } = TimeSpan.FromMilliseconds(1500);
    
    private bool _isVisible = false;
    public bool IsVisible
    {
        get => _isVisible;
        set => this.RaiseAndSetIfChanged(ref _isVisible, value);
    }
    
    private bool _isAnimating = false;
    public bool IsAnimating
    {
        get => _isAnimating;
        set => this.RaiseAndSetIfChanged(ref _isAnimating, value);
    }
    
    public ReactiveCommand<Unit, Unit> RequestManualRetryCommand { get; }
    
    public void SetView(NetworkStatusNotification view)
    {
        _view = view;
        _statusEllipse = view.FindControl<Ellipse>("StatusEllipse");
        CreateAnimations();
    }

    public NetworkStatusNotificationViewModel(ILocalizationService localizationService, INetworkEvents networkEvents)
    {
        LocalizationService = localizationService;
        _networkEvents = networkEvents;

        _networkEvents.NetworkStatusChanged
            .Subscribe((evt) =>
            {
                // Marshal to UI thread for all UI operations
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    switch (evt.State)
                    {
                        case NetworkStatus.RetriesExhausted:
                        case NetworkStatus.DataCenterDisconnected:
                            if (!IsVisible)
                            {
                                await ShowAsync();
                            }
                            break;
                        case NetworkStatus.DataCenterConnected:
                            if (IsVisible)
                            {
                                await HideAsync();
                            }
                            break;
                    }
                });
            });
        
        RequestManualRetryCommand = ReactiveCommand.Create(() =>
        {
            // Ensure this is also on UI thread if needed
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                _networkEvents.RequestManualRetry(ManualRetryRequestedEvent.New());
            });
        });
    }
    
    private void CreateAnimations()
    {
        if (_view == null) return;
        
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
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(TranslateTransform.YProperty, -20d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
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
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(TranslateTransform.YProperty, 0d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
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
                    Setters = { new Setter(Visual.OpacityProperty, 0.3d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.5d),
                    Setters = { new Setter(Visual.OpacityProperty, 1d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(Visual.OpacityProperty, 0.3d) }
                }
            }
        };
    }

    private async Task ShowAsync()
    {
        if (_view == null || IsAnimating) return;
        
        IsAnimating = true;
        
        // Set up the transform
        _view.RenderTransform = new TranslateTransform();
        
        // Make visible before animation
        IsVisible = true;
        
        if (_appearAnimation == null)
        {
            CreateAnimations();
        }

        await _appearAnimation!.RunAsync(_view);
        
        StartFlickerAnimation();
        IsAnimating = false;
    }

    private async Task HideAsync()
    {
        if (_view == null || IsAnimating) return;
        
        IsAnimating = true;
        
        StopFlickerAnimation();
        
        if (_disappearAnimation == null)
        {
            CreateAnimations();
        }

        await _disappearAnimation!.RunAsync(_view);
        
        // Hide after animation completes
        IsVisible = false;
        IsAnimating = false;
    }

    private void StartFlickerAnimation()
    {
        if (_flickerAnimation == null || _statusEllipse == null)
        {
            CreateAnimations();
        }

        _flickerAnimation?.RunAsync(_statusEllipse!);
    }

    private void StopFlickerAnimation()
    {
        // Ensure we're on the UI thread for UI operations
        if (Dispatcher.UIThread.CheckAccess())
        {
            if (_statusEllipse != null)
            {
                _statusEllipse.Opacity = 1.0;
            }
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_statusEllipse != null)
                {
                    _statusEllipse.Opacity = 1.0;
                }
            });
        }
    }
}