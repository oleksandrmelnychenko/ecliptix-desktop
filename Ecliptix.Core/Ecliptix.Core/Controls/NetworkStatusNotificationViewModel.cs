using Ecliptix.Core.Services;
using ReactiveUI;

namespace Ecliptix.Core.Controls;

public class NetworkStatusNotificationViewModel : ReactiveObject
{
    public ILocalizationService LocalizationService { get; }

    private bool _isConnected = true;
    public bool IsConnected
    {
        get => _isConnected;
        set => this.RaiseAndSetIfChanged(ref _isConnected, value);
    }
    
    public NetworkStatusNotificationViewModel(ILocalizationService localizationService)
    {
        LocalizationService = localizationService;
    }
    
    public void ChangeNetworkStatus(bool isConnected)
    {
        IsConnected = isConnected;
    }
}