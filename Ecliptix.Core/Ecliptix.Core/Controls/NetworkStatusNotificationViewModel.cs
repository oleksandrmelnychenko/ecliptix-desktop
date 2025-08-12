using System;
using System.Reactive;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.Services;
using ReactiveUI;

namespace Ecliptix.Core.Controls;

public sealed class NetworkStatusNotificationViewModel : ReactiveObject
{
    public ILocalizationService LocalizationService { get; }
    
    private readonly INetworkEvents _networkEvents;
    
    private bool _isVisible = false;
    public bool IsVisible
    {
        get => _isVisible;
        set => this.RaiseAndSetIfChanged(ref _isVisible, value);
    }
    
    public ReactiveCommand<Unit, Unit> RequestManualRetryCommand { get; }
    

    public NetworkStatusNotificationViewModel(ILocalizationService localizationService, INetworkEvents networkEvents)
    {
        LocalizationService = localizationService;
        _networkEvents = networkEvents;

        _networkEvents.NetworkStatusChanged
            .Subscribe((evt =>
            {
                switch (evt.State)
                {
                    case NetworkStatus.RetriesExhausted:
                    case NetworkStatus.DataCenterDisconnected:
                        IsVisible = true;
                        break;
                    case NetworkStatus.DataCenterConnected:
                        IsVisible = false;
                        break;
                }
            }));
        
        RequestManualRetryCommand = ReactiveCommand.Create(() =>
        {
            _networkEvents.RequestManualRetry(ManualRetryRequestedEvent.New());
        });
        
    }
    
}