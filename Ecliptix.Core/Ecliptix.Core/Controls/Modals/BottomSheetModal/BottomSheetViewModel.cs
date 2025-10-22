using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Services;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Modals.BottomSheetModal;

public sealed class BottomSheetViewModel : ReactiveObject, IActivatableViewModel, IDisposable
{
    private bool _disposed;
    private bool _isVisible;
    private bool _isDismissableOnScrimClick;
    private bool _showScrim;
    private readonly IBottomSheetService _bottomSheetService;

    private UserControl? _content;

    public UserControl? Content
    {
        get => _content;
        set
        {
            if (_content != null && _content != value)
            {
                UserControl? oldContent = _content;

                Dispatcher.UIThread.Post(() =>
                {
                    if (oldContent.DataContext is IDisposable disposableContext)
                    {
                        disposableContext.Dispose();
                    }
                });
            }
            this.RaiseAndSetIfChanged(ref _content, value);
        }
    }

    public bool ShowScrim
    {
        get => _showScrim;
        set => this.RaiseAndSetIfChanged(ref _showScrim, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => this.RaiseAndSetIfChanged(ref _isVisible, value);
    }

    public bool IsDismissableOnScrimClick
    {
        get => _isDismissableOnScrimClick;
        set => this.RaiseAndSetIfChanged(ref _isDismissableOnScrimClick, value);
    }

    public ViewModelActivator Activator { get; } = new();

    public ReactiveCommand<Unit, Unit> ShowCommand { get; }

    public ReactiveCommand<Unit, Unit> HideCommand { get; }

    public ReactiveCommand<Unit, Unit> ToggleCommand { get; }

    public BottomSheetViewModel(
        IBottomSheetService bottomSheetService)
    {
        _bottomSheetService = bottomSheetService;
        _content = null;
        _isVisible = false;
        _isDismissableOnScrimClick = true;

        ShowCommand = ReactiveCommand.Create(() =>
        {
            return Unit.Default;
        });

        HideCommand = ReactiveCommand.Create(() =>
        {
            IsVisible = false;
            return Unit.Default;
        });

        ToggleCommand = ReactiveCommand.Create(() =>
        {
            bool newVisibility = !IsVisible;
            IsVisible = newVisibility;
            return Unit.Default;
        });

        this.WhenActivated(disposables =>
        {
            _bottomSheetService.OnBottomSheetChanged(eventArgs =>
                {
                    bool shouldBeVisible = eventArgs.Control != null;

                    if (shouldBeVisible)
                    {
                        if (_content != eventArgs.Control)
                        {
                            Content = eventArgs.Control;
                        }
                    }

                    if (_showScrim != eventArgs.ShowScrim)
                    {
                        ShowScrim = eventArgs.ShowScrim;
                    }

                    if (_isDismissableOnScrimClick != eventArgs.IsDismissable)
                    {
                        IsDismissableOnScrimClick = eventArgs.IsDismissable;
                    }

                    if (_isVisible != shouldBeVisible)
                    {
                        IsVisible = shouldBeVisible;
                    }

                    if (!shouldBeVisible && _content != null)
                    {
                        Observable.Timer(BottomSheetAnimationConstants.HideAnimationDuration)
                            .ObserveOn(RxApp.MainThreadScheduler)
                            .Subscribe(_ => Content = null)
                            .DisposeWith(disposables);
                    }

                    return System.Threading.Tasks.Task.CompletedTask;

                }, SubscriptionLifetime.Scoped)
                .DisposeWith(disposables);
        });
    }

    public void BottomSheetDismissed()
    {
        _bottomSheetService.BottomSheetDismissed();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
