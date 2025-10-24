using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using ReactiveUI;
using Serilog;
using IMessageBus = Ecliptix.Core.Core.Messaging.IMessageBus;

namespace Ecliptix.Core.Controls.Modals.BottomSheetModal;

public sealed class BottomSheetViewModel : ReactiveObject, IActivatableViewModel, IDisposable
{
    private readonly IBottomSheetService _bottomSheetService;
    private readonly IMessageBus _messageBus;
    private bool _disposed;
    private bool _isVisible;
    private bool _isDismissableOnScrimClick;
    private bool _showScrim;
    private UserControl? _content;

    public UserControl? Content
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

    public BottomSheetViewModel(IBottomSheetService bottomSheetService, IMessageBus messageBus)
    {
        _bottomSheetService = bottomSheetService;
        _messageBus = messageBus;

        this.WhenActivated(disposables =>
        {
            _messageBus.Subscribe<BottomSheetCommandEvent>(async evt =>
            {
                await HandleCommand(evt);
            }).DisposeWith(disposables);

            this.WhenAnyValue(x => x.IsVisible)
                .Skip(1)
                .Subscribe(async isVisible =>
                {
                    UserControl? contentSnapshot = Content;

                    await Task.Delay(isVisible
                        ? BottomSheetAnimationConstants.ShowAnimationDuration
                        : BottomSheetAnimationConstants.HideAnimationDuration);

                    await _messageBus.PublishAsync(isVisible
                        ? BottomSheetAnimationCompleteEvent.ShowComplete()
                        : BottomSheetAnimationCompleteEvent.HideComplete());

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

    private async Task HandleCommand(BottomSheetCommandEvent evt)
    {
        if (_disposed)
        {
            return;
        }

        if (evt.AnimationType == AnimationType.Show)
        {
            Content = evt.Control;
            ShowScrim = evt.ShowScrim;
            IsDismissableOnScrimClick = evt.IsDismissable;
            IsVisible = true;
        }
        else
        {
            IsVisible = false;
        }
    }

    public void BottomSheetDismissed()
    {
        Task.Run(async () => await _bottomSheetService.BottomSheetDismissed());
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
