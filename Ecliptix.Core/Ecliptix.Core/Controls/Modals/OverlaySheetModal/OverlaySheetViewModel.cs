using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Ecliptix.Core.Messaging.Core.Messaging.Events;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using ReactiveUI;
using IMessageBus = Ecliptix.Core.Messaging.Core.Messaging.IMessageBus;

namespace Ecliptix.Core.Controls.Modals.OverlaySheetModal;

public sealed class OverlaySheetViewModel : ReactiveObject, IActivatableViewModel, IDisposable
{
    private readonly IOverlaySheetService _overlaySheetService;
    private bool _disposed;
    private bool _isVisible;
    private bool _isDismissableOnScrimClick;
    private bool _showScrim;
    private object? _content;

    public object? Content
    {
        get => _content;
        set => this.RaiseAndSetIfChanged(ref _content, value);
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

    public OverlaySheetViewModel(IOverlaySheetService overlaySheetService, IMessageBus messageBus)
    {
        _overlaySheetService = overlaySheetService;
        IMessageBus messageBus1 = messageBus;

        this.WhenActivated(disposables =>
        {
            messageBus1.Subscribe<OverlaySheetCommandEvent>(async evt =>
            {
                await HandleCommand(evt);
            }).DisposeWith(disposables);

            this.WhenAnyValue(x => x.IsVisible)
                .Skip(1)
                .Subscribe(async isVisible =>
                {
                    object? contentSnapshot = Content;

                    await Task.Delay(isVisible
                        ? OverlaySheetAnimationConstants.ShowAnimationDuration
                        : OverlaySheetAnimationConstants.HideAnimationDuration);

                    await messageBus1.PublishAsync(isVisible
                        ? OverlaySheetAnimationCompleteEvent.ShowComplete()
                        : OverlaySheetAnimationCompleteEvent.HideComplete());

                    if (!isVisible)
                    {
                        await Task.Delay(50);

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (ReferenceEquals(Content, contentSnapshot))
                            {
                                Content = null;
                            }
                        });
                    }
                })
                .DisposeWith(disposables);
        });
    }

    private Task HandleCommand(OverlaySheetCommandEvent evt)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        if (evt.AnimationType == OverlayAnimationType.SHOW)
        {
            Content = evt.ViewModel;
            ShowScrim = evt.ShowScrim;
            IsDismissableOnScrimClick = evt.IsDismissable;
            IsVisible = true;
        }
        else
        {
            IsVisible = false;
        }

        return Task.CompletedTask;
    }

    public void OverlaySheetDismissed() => Task.Run(async () => await _overlaySheetService.OverlaySheetDismissed());

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
